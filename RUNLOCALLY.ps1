param(
    [ValidateSet('Start', 'Stop', 'Restart', 'Status', 'Logs', 'Test')]
    [string]$Action = 'Start'
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = $PSScriptRoot
$ComposeFile = Join-Path $ProjectRoot 'docker-compose.yml'
$EnvFile = Join-Path $ProjectRoot '.env'
$CredentialFile = Join-Path $ProjectRoot '.local-credentials.txt'

function Fail([string]$Message, [int]$Code = 1) {
    Write-Error $Message
    exit $Code
}

function New-RandomSecret([int]$Bytes = 48) {
    $Buffer = [byte[]]::new($Bytes)
    $Generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $Generator.GetBytes($Buffer) } finally { $Generator.Dispose() }
    return [Convert]::ToBase64String($Buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Ensure-Configuration {
    if (Test-Path -LiteralPath $EnvFile) { return }
    $Example = Join-Path $ProjectRoot '.env.example'
    if (-not (Test-Path -LiteralPath $Example)) { Fail 'Missing .env.example. Restore the project configuration file.' }
    $PostgresPassword = New-RandomSecret 36
    $JwtSecret = New-RandomSecret 48
    $AdminPassword = (New-RandomSecret 30) + 'Aa9!'
    $AdminEmail = 'admin@orderflow.local'
    $Configuration = Get-Content -LiteralPath $Example -Raw
    $Configuration = $Configuration -replace '(?m)^POSTGRES_PASSWORD=.*$', "POSTGRES_PASSWORD=$PostgresPassword"
    $Configuration = $Configuration -replace '(?m)^JWT_SIGNING_KEY=.*$', "JWT_SIGNING_KEY=$JwtSecret"
    $Configuration = $Configuration -replace '(?m)^BOOTSTRAP_ADMIN_EMAIL=.*$', "BOOTSTRAP_ADMIN_EMAIL=$AdminEmail"
    $Configuration = $Configuration -replace '(?m)^BOOTSTRAP_ADMIN_PASSWORD=.*$', "BOOTSTRAP_ADMIN_PASSWORD=$AdminPassword"
    [System.IO.File]::WriteAllText($EnvFile, $Configuration, [System.Text.UTF8Encoding]::new($false))
    $Credentials = @"
OrderFlow local administrator
Email: $AdminEmail
Password: $AdminPassword

Keep this file private. It is excluded from Git.
"@
    [System.IO.File]::WriteAllText($CredentialFile, $Credentials, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Created private local configuration at $EnvFile. Administrator credentials were saved to $CredentialFile." -ForegroundColor Green
}

function Read-Configuration {
    $Values = @{}
    foreach ($Line in Get-Content -LiteralPath $EnvFile) {
        if ($Line -match '^\s*([^#=\s]+)\s*=\s*(.*)\s*$') { $Values[$Matches[1]] = $Matches[2].Trim('"').Trim("'") }
    }
    foreach ($Name in @('POSTGRES_PASSWORD', 'JWT_SIGNING_KEY', 'BOOTSTRAP_ADMIN_EMAIL', 'BOOTSTRAP_ADMIN_PASSWORD')) {
        if ([string]::IsNullOrWhiteSpace($Values[$Name])) { Fail "Required setting '$Name' is empty in .env. Fill it and run again." }
    }
    if ([Text.Encoding]::UTF8.GetByteCount($Values.JWT_SIGNING_KEY) -lt 32) { Fail 'JWT_SIGNING_KEY must contain at least 32 UTF-8 bytes.' }
    return $Values
}

function Invoke-Compose([string[]]$Arguments) {
    & docker compose --project-directory $ProjectRoot --project-name orderflow --env-file $EnvFile -f $ComposeFile @Arguments | Out-Host
    return [int]$LASTEXITCODE
}

function Invoke-LocalSmokeTests([int]$Port, [hashtable]$Settings) {
    $BaseUrl = "http://localhost:$Port"
    $AdminLogin = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{ email = $Settings.BOOTSTRAP_ADMIN_EMAIL; password = $Settings.BOOTSTRAP_ADMIN_PASSWORD } | ConvertTo-Json -Compress)
    $AdminHeaders = @{ Authorization = "Bearer $($AdminLogin.accessToken)" }
    $EmployeeEmail = "local-$(New-Guid)@example.test"
    $EmployeePassword = (New-RandomSecret 32) + 'Aa9!'
    Invoke-RestMethod -Uri "$BaseUrl/api/auth/register" -Method Post -ContentType 'application/json' -Body (@{ email = $EmployeeEmail; password = $EmployeePassword; displayName = 'Local Smoke Test' } | ConvertTo-Json -Compress) | Out-Null
    $EmployeeLogin = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{ email = $EmployeeEmail; password = $EmployeePassword } | ConvertTo-Json -Compress)
    $EmployeeHeaders = @{ Authorization = "Bearer $($EmployeeLogin.accessToken)" }
    $Customer = Invoke-RestMethod -Uri "$BaseUrl/api/customers" -Method Post -Headers $EmployeeHeaders -ContentType 'application/json' -Body (@{ name = 'Local Smoke Customer'; email = "customer-$(New-Guid)@example.test" } | ConvertTo-Json -Compress)
    Invoke-WebRequest -Uri "$BaseUrl/swagger/index.html" -TimeoutSec 10 -UseBasicParsing | Out-Null
    $Sku = 'LOCAL-' + (New-Guid).ToString('N').Substring(0, 16).ToUpperInvariant()
    $Product = Invoke-RestMethod -Uri "$BaseUrl/api/products" -Method Post -Headers $AdminHeaders -ContentType 'application/json' -Body (@{ sku = $Sku; name = 'Local Smoke Product'; price = 8.25; stock = 10 } | ConvertTo-Json -Compress)
    Invoke-RestMethod -Uri "$BaseUrl/api/inventory/transactions" -Method Post -Headers $EmployeeHeaders -ContentType 'application/json' -Body (@{ productId = $Product.id; quantity = 5; type = 0; reference = 'RUNLOCALLY smoke test' } | ConvertTo-Json -Compress) | Out-Null
    $Order = Invoke-RestMethod -Uri "$BaseUrl/api/orders" -Method Post -Headers $EmployeeHeaders -ContentType 'application/json' -Body (@{ customerId = $Customer.id; items = @(@{ productId = $Product.id; quantity = 2 }) } | ConvertTo-Json -Compress -Depth 5)
    if ([decimal]$Order.total -ne 16.50) { throw 'Smoke test failed: server-side order total was incorrect.' }
    $Reserved = Invoke-RestMethod -Uri "$BaseUrl/api/products/$($Product.id)" -Headers $EmployeeHeaders
    if ([int]$Reserved.availableStock -ne 13) { throw 'Smoke test failed: order did not reserve inventory.' }
    foreach ($Status in @(1, 2, 3, 4)) { Invoke-RestMethod -Uri "$BaseUrl/api/orders/$($Order.id)/status" -Method Patch -Headers $EmployeeHeaders -ContentType 'application/json' -Body (@{ status = $Status } | ConvertTo-Json -Compress) | Out-Null }
    $StatusHistory = Invoke-RestMethod -Uri "$BaseUrl/api/orders/$($Order.id)/history" -Headers $EmployeeHeaders
    $StatusHistoryCount = if ($StatusHistory -is [System.Collections.ICollection]) { $StatusHistory.Count } else { 1 }
    if ($StatusHistoryCount -ne 5) { throw 'Smoke test failed: order status history was not persisted.' }
    $CancelledOrder = Invoke-RestMethod -Uri "$BaseUrl/api/orders" -Method Post -Headers $EmployeeHeaders -ContentType 'application/json' -Body (@{ customerId = $Customer.id; items = @(@{ productId = $Product.id; quantity = 3 }) } | ConvertTo-Json -Compress -Depth 5)
    Invoke-RestMethod -Uri "$BaseUrl/api/orders/$($CancelledOrder.id)/status" -Method Patch -Headers $EmployeeHeaders -ContentType 'application/json' -Body (@{ status = 5 } | ConvertTo-Json -Compress) | Out-Null
    $Released = Invoke-RestMethod -Uri "$BaseUrl/api/products/$($Product.id)" -Headers $EmployeeHeaders
    if ([int]$Released.availableStock -ne 13) { throw 'Smoke test failed: cancelling an order did not release inventory.' }
    $History = Invoke-RestMethod -Uri "$BaseUrl/api/inventory/products/$($Product.id)/transactions" -Headers $EmployeeHeaders
    if ($History.totalCount -lt 4) { throw 'Smoke test failed: inventory transaction history is incomplete.' }
    $Analytics = Invoke-RestMethod -Uri "$BaseUrl/api/analytics/overview" -Headers $AdminHeaders
    if ([int]$Analytics.orderCount -lt 1 -or [decimal]$Analytics.sales -lt 16.50) { throw 'Smoke test failed: sales analytics did not include created orders.' }
    Write-Host 'API smoke checks passed: employee registration/login, admin login, customer/product creation, receipt, order total/reservation, status flow, cancellation/release, inventory history, and analytics.' -ForegroundColor Green
}

function Test-PortAvailable([int]$Port) {
    $Listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try { $Listener.Start(); return $true } catch { return $false } finally { $Listener.Stop() }
}

function Wait-ForDocker {
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $Os = Get-CimInstance Win32_OperatingSystem
        if ($Os.Caption -notmatch 'Windows 11') { Write-Warning "Detected $($Os.Caption). RUNLOCALLY targets Windows 11; continuing because Docker may still work." }
    }
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Host 'Docker CLI is missing. Docker Desktop is required to run OrderFlow locally.' -ForegroundColor Yellow
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            $Answer = Read-Host 'Install the official Docker Desktop package with winget now? [y/N]'
            if ($Answer -match '^(y|yes)$') {
                & winget install --id Docker.DockerDesktop --exact --source winget
                if ($LASTEXITCODE -ne 0) { Fail 'Docker Desktop installation failed. Install it manually from https://www.docker.com/products/docker-desktop/ and rerun this script.' }
                Fail 'Docker Desktop installer completed. Launch Docker Desktop manually, complete its first-run setup, then rerun RUNLOCALLY.bat.'
            }
        }
        Fail 'Install Docker Desktop from https://www.docker.com/products/docker-desktop/, launch it, then rerun RUNLOCALLY.bat.'
    }
    docker compose version *> $null
    if ($LASTEXITCODE -ne 0) { Fail 'Docker Compose is unavailable. Update Docker Desktop and make sure Docker Compose v2 is enabled.' }
    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        $DesktopPaths = @("$env:ProgramFiles\Docker\Docker\Docker Desktop.exe", "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe")
        $Desktop = $DesktopPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($Desktop) { Write-Host 'Docker daemon is stopped. Starting Docker Desktop and waiting up to 120 seconds.'; Start-Process -FilePath $Desktop -WindowStyle Hidden }
        else { Write-Host 'Docker daemon is stopped. Start Docker Desktop from the Start menu.' }
        $Ready = $false
        for ($i = 0; $i -lt 24; $i++) { Start-Sleep -Seconds 5; docker info *> $null; if ($LASTEXITCODE -eq 0) { $Ready = $true; break } }
        if (-not $Ready) { Fail 'Docker daemon did not become ready within 120 seconds. Start Docker Desktop manually and rerun.' }
    }
}

function Start-OrderFlow {
    if (-not (Test-Path -LiteralPath $ComposeFile)) { Fail "Missing docker-compose.yml in $ProjectRoot." }
    Wait-ForDocker
    Ensure-Configuration
    $Settings = Read-Configuration
    $ApiPort = [int]$Settings.ORDERFLOW_API_PORT
    $PgPort = [int]$Settings.POSTGRES_PORT
    foreach ($Port in @($ApiPort, $PgPort)) { if ($Port -lt 1 -or $Port -gt 65535) { Fail 'ORDERFLOW_API_PORT and POSTGRES_PORT must be integers between 1 and 65535.' } }
    $Containers = docker ps -a --filter 'label=com.docker.compose.project=orderflow' --format '{{.Names}}'
    if ($Containers) { Write-Host "Detected existing OrderFlow containers: $($Containers -join ', ')" }
    $ExistingVolume = docker volume ls --filter 'name=orderflow_postgres-data' --format '{{.Name}}' | Select-Object -First 1
    if ($ExistingVolume) { Write-Host "Existing PostgreSQL volume '$ExistingVolume' will be preserved and reused." }
    foreach ($Port in @($ApiPort, $PgPort)) {
        if (-not (Test-PortAvailable $Port)) {
            # Avoid a nested-quote Go template in the native PowerShell argument.
            # Read IDs with a simple format, then inspect each container's labels as JSON.
            $PortContainerIds = @(docker ps --filter "publish=$Port" --format '{{.ID}}' 2>$null)
            $ProjectOnPort = foreach ($ContainerId in $PortContainerIds) {
                $LabelsJson = docker inspect --format '{{json .Config.Labels}}' $ContainerId 2>$null
                if ($LabelsJson) {
                    try { ($LabelsJson | ConvertFrom-Json).'com.docker.compose.project' } catch { $null }
                }
            }
            if ($ProjectOnPort -notcontains 'orderflow') { Fail "Port $Port is already in use. Change its value in .env or stop the other service." }
        }
    }
    & docker compose --project-directory $ProjectRoot --project-name orderflow --env-file $EnvFile -f $ComposeFile config --quiet
    if ($LASTEXITCODE -ne 0) { Fail 'docker-compose.yml or .env is invalid. Review the configuration errors above.' }
    Write-Host 'Building the OrderFlow API image...'
    if ((Invoke-Compose @('build', 'api')) -ne 0) { Show-Failure 'API image build' }
    Write-Host 'Starting PostgreSQL...'
    if ((Invoke-Compose @('up', '-d', 'db')) -ne 0) { Show-Failure 'PostgreSQL startup' }
    $DbReady = $false
    for ($i = 0; $i -lt 36; $i++) {
        $Id = docker compose --project-directory $ProjectRoot --project-name orderflow --env-file $EnvFile -f $ComposeFile ps -q db
        if ($Id) { $Health = docker inspect --format '{{.State.Health.Status}}' $Id 2>$null; if ($Health -eq 'healthy') { $DbReady = $true; break } }
        Start-Sleep -Seconds 5
    }
    if (-not $DbReady) { Show-Failure 'PostgreSQL health check timed out after 180 seconds' }
    Write-Host 'Starting OrderFlow API and applying the configured database bootstrap...'
    if ((Invoke-Compose @('up', '-d', 'api')) -ne 0) { Show-Failure 'API startup' }
    $HealthUrl = "http://localhost:$ApiPort/health"
    $Healthy = $false
    for ($i = 0; $i -lt 36; $i++) { try { $Reply = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 4; if ($Reply.status -eq 'Healthy') { $Healthy = $true; break } } catch { }; Start-Sleep -Seconds 5 }
    if (-not $Healthy) { Show-Failure 'API did not become healthy within 180 seconds' }
    $Volume = docker volume ls --filter 'name=orderflow_postgres-data' --format '{{.Name}}' | Select-Object -First 1
    if ($Volume) { Write-Host "Using persistent PostgreSQL volume '$Volume'. Existing data is retained." }
    $MigrationCount = docker compose --project-directory $ProjectRoot --project-name orderflow --env-file $EnvFile -f $ComposeFile exec -T db psql -U orderflow -d orderflow -Atc 'SELECT count(*) FROM "__EFMigrationsHistory";' 2>$null
    if ($LASTEXITCODE -ne 0 -or [int]$MigrationCount -lt 1) { Show-Failure 'EF Core migration history verification' }
    Invoke-LocalSmokeTests $ApiPort $Settings
    Write-Host "OrderFlow is healthy. API: http://localhost:$ApiPort | Swagger: http://localhost:$ApiPort/swagger | Health: $HealthUrl" -ForegroundColor Green
}

function Show-Failure([string]$Component) {
    Write-Host "Startup failed: $Component" -ForegroundColor Red
    & docker compose --project-directory $ProjectRoot --project-name orderflow --env-file $EnvFile -f $ComposeFile logs --tail 100 db api
    Fail "Review the logs above. Persistent PostgreSQL data was not deleted. Try RUNLOCALLY.bat -Action Logs after resolving the error."
}

function Run-Tests {
    $Sdk = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($Sdk) {
        Push-Location $ProjectRoot
        try {
            & dotnet restore OrderFlow.slnx
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            & dotnet build OrderFlow.slnx --configuration Release --no-restore
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            & dotnet test OrderFlow.slnx --configuration Release --no-build --logger 'trx;LogFileName=OrderFlow.trx' --results-directory TestResults
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            Write-Host 'Release build and test suite passed.' -ForegroundColor Green
            exit 0
        } finally { Pop-Location }
    }
    Wait-ForDocker
    $Mount = "$($ProjectRoot):/src"
    & docker run --rm -v $Mount -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet restore OrderFlow.slnx
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & docker run --rm -v $Mount -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build OrderFlow.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & docker run --rm -v $Mount -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test OrderFlow.slnx --configuration Release --no-build --logger 'trx;LogFileName=OrderFlow.trx' --results-directory TestResults
    exit $LASTEXITCODE
}

try {
    switch ($Action) {
        'Start' { Start-OrderFlow }
        'Stop' { Wait-ForDocker; if (Test-Path $EnvFile) { if ((Invoke-Compose @('stop')) -ne 0) { exit $LASTEXITCODE } } else { & docker compose --project-directory $ProjectRoot --project-name orderflow -f $ComposeFile stop; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }; Write-Host 'OrderFlow stopped. PostgreSQL volume was preserved.' }
        'Restart' {
            Wait-ForDocker
            $Before = -1
            if (Test-Path $EnvFile) {
                $Current = docker compose --project-directory $ProjectRoot --project-name orderflow --env-file $EnvFile -f $ComposeFile ps -q db
                if ($Current) { $Before = [int](docker compose --project-directory $ProjectRoot --project-name orderflow --env-file $EnvFile -f $ComposeFile exec -T db psql -U orderflow -d orderflow -Atc 'SELECT count(*) FROM "Orders";' 2>$null) }
                Invoke-Compose @('stop') | Out-Null
            }
            Start-OrderFlow
            if ($Before -ge 0) {
                $After = [int](docker compose --project-directory $ProjectRoot --project-name orderflow --env-file $EnvFile -f $ComposeFile exec -T db psql -U orderflow -d orderflow -Atc 'SELECT count(*) FROM "Orders";' 2>$null)
                if ($After -lt $Before) { Fail 'Database persistence verification failed after restart.' }
                Write-Host "Database persistence verified: order count remained at least $Before after restart." -ForegroundColor Green
            }
        }
        'Status' { Wait-ForDocker; if (Test-Path $EnvFile) { if ((Invoke-Compose @('ps')) -ne 0) { exit $LASTEXITCODE } } else { Write-Host 'OrderFlow has no local .env configuration yet.' } }
        'Logs' { Wait-ForDocker; if (Test-Path $EnvFile) { if ((Invoke-Compose @('logs', '--tail', '100', '-f')) -ne 0) { exit $LASTEXITCODE } } else { Write-Host 'OrderFlow has no local .env configuration yet.' } }
        'Test' { Run-Tests }
    }
} catch {
    Write-Host "RUNLOCALLY failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
