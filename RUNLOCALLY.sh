#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE="$ROOT/docker-compose.yml"
ENV_FILE="$ROOT/.env"
ACTION="${1:-Start}"
trap 'status=$?; if (( status != 0 )); then printf "RUNLOCALLY failed at line %s (exit %s). Existing database data was preserved.\n" "$LINENO" "$status" >&2; fi' ERR

compose() { docker compose --project-directory "$ROOT" --project-name orderflow --env-file "$ENV_FILE" -f "$COMPOSE" "$@"; }
fail() { printf 'Error: %s\n' "$1" >&2; exit 1; }
require_docker() {
  command -v docker >/dev/null 2>&1 || fail 'Docker CLI is missing. Install Docker Engine and Compose v2, then rerun.'
  docker compose version >/dev/null 2>&1 || fail 'Docker Compose v2 is missing or unavailable.'
  docker info >/dev/null 2>&1 || fail 'Docker daemon is stopped. Start Docker Engine/Desktop, then rerun.'
  command -v curl >/dev/null 2>&1 || fail 'curl is required for API health and smoke checks.'
}
ensure_env() {
  if [[ ! -f "$ENV_FILE" ]]; then
    [[ -f "$ROOT/.env.example" ]] || fail 'Missing .env.example.'
    command -v openssl >/dev/null 2>&1 || fail 'OpenSSL is required to generate local secrets.'
    POSTGRES_PASSWORD="$(openssl rand -hex 36)"
    JWT_SIGNING_KEY="$(openssl rand -base64 64 | tr -d '\n')"
    BOOTSTRAP_ADMIN_PASSWORD="$(openssl rand -base64 30 | tr -d '\n')Aa9!"
    BOOTSTRAP_ADMIN_EMAIL='admin@orderflow.local'
    umask 077
    cat > "$ENV_FILE" <<EOF
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
JWT_SIGNING_KEY=$JWT_SIGNING_KEY
BOOTSTRAP_ADMIN_EMAIL=$BOOTSTRAP_ADMIN_EMAIL
BOOTSTRAP_ADMIN_PASSWORD=$BOOTSTRAP_ADMIN_PASSWORD
ORDERFLOW_API_PORT=8080
POSTGRES_PORT=5432
EOF
    cat > "$ROOT/.local-credentials.txt" <<EOF
OrderFlow local administrator
Email: $BOOTSTRAP_ADMIN_EMAIL
Password: $BOOTSTRAP_ADMIN_PASSWORD

Keep this file private. It is excluded from Git.
EOF
    chmod 600 "$ENV_FILE" "$ROOT/.local-credentials.txt"
    printf 'Created .env and private local administrator credentials file.\n'
  fi
  for key in POSTGRES_PASSWORD JWT_SIGNING_KEY BOOTSTRAP_ADMIN_EMAIL BOOTSTRAP_ADMIN_PASSWORD; do
    grep -Eq "^${key}=.+$" "$ENV_FILE" || fail "Required setting $key is empty or missing in .env."
  done
  local jwt
  jwt="$(sed -n 's/^JWT_SIGNING_KEY=//p' "$ENV_FILE" | head -n1)"
  [[ ${#jwt} -ge 32 ]] || fail 'JWT_SIGNING_KEY must be at least 32 characters.'
}
wait_db() {
  printf 'Waiting for PostgreSQL health check (up to 180 seconds)...\n'
  for _ in $(seq 1 36); do
    if [[ "$(compose ps --format json db 2>/dev/null | grep -o '"Health":"[^"]*"' | head -n1 | cut -d'"' -f4)" == healthy ]] || compose exec -T db pg_isready -U orderflow -d orderflow >/dev/null 2>&1; then return 0; fi
    sleep 5
  done
  compose logs --tail 100 db
  fail 'PostgreSQL did not become ready. Check database logs above.'
}
api_smoke() {
  local base="http://localhost:$1" admin_email admin_password employee_email employee_password admin_token employee_token customer_json customer_id sku product_json product_id order_json order_id cancelled_json cancelled_id available total history_count analytics_sales
  admin_email="$(sed -n 's/^BOOTSTRAP_ADMIN_EMAIL=//p' "$ENV_FILE" | head -n1)"
  admin_password="$(sed -n 's/^BOOTSTRAP_ADMIN_PASSWORD=//p' "$ENV_FILE" | head -n1)"
  admin_token="$(curl -fsS -H 'Content-Type: application/json' -d "{\"email\":\"$admin_email\",\"password\":\"$admin_password\"}" "$base/api/auth/login" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')"
  [[ -n "$admin_token" ]] || fail 'Admin login smoke test failed.'
  employee_email="local-$(openssl rand -hex 8)@example.test"
  employee_password="$(openssl rand -base64 30 | tr -d '\n')Aa9!"
  curl -fsS -H 'Content-Type: application/json' -d "{\"email\":\"$employee_email\",\"password\":\"$employee_password\",\"displayName\":\"Local Smoke Test\"}" "$base/api/auth/register" >/dev/null
  employee_token="$(curl -fsS -H 'Content-Type: application/json' -d "{\"email\":\"$employee_email\",\"password\":\"$employee_password\"}" "$base/api/auth/login" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')"
  [[ -n "$employee_token" ]] || fail 'Employee login smoke test failed.'
  customer_json="$(curl -fsS -H "Authorization: Bearer $employee_token" -H 'Content-Type: application/json' -d "{\"name\":\"Local Smoke Customer\",\"email\":\"customer-$(openssl rand -hex 8)@example.test\"}" "$base/api/customers")"
  customer_id="$(sed -n 's/.*"id":"\([^"]*\)".*/\1/p' <<<"$customer_json")"
  curl -fsS "$base/swagger/index.html" >/dev/null
  sku="LOCAL-$(openssl rand -hex 8 | tr '[:lower:]' '[:upper:]')"
  product_json="$(curl -fsS -H "Authorization: Bearer $admin_token" -H 'Content-Type: application/json' -d "{\"sku\":\"$sku\",\"name\":\"Local Smoke Product\",\"price\":8.25,\"stock\":10}" "$base/api/products")"
  product_id="$(sed -n 's/.*"id":"\([^"]*\)".*/\1/p' <<<"$product_json")"
  [[ -n "$customer_id" && -n "$product_id" ]] || fail 'Customer or product creation smoke test failed.'
  curl -fsS -H "Authorization: Bearer $employee_token" -H 'Content-Type: application/json' -d "{\"productId\":\"$product_id\",\"quantity\":5,\"type\":0,\"reference\":\"RUNLOCALLY smoke test\"}" "$base/api/inventory/transactions" >/dev/null
  order_json="$(curl -fsS -H "Authorization: Bearer $employee_token" -H 'Content-Type: application/json' -d "{\"customerId\":\"$customer_id\",\"items\":[{\"productId\":\"$product_id\",\"quantity\":2}]}" "$base/api/orders")"
  order_id="$(sed -n 's/.*"id":"\([^"]*\)".*/\1/p' <<<"$order_json")"
  total="$(sed -n 's/.*"total":\([0-9.]*\).*/\1/p' <<<"$order_json")"
  [[ "$total" == 16.5 || "$total" == 16.50 ]] || fail 'Server-calculated order total smoke test failed.'
  available="$(curl -fsS -H "Authorization: Bearer $employee_token" "$base/api/products/$product_id" | sed -n 's/.*"availableStock":\([0-9]*\).*/\1/p')"
  [[ "$available" == 13 ]] || fail 'Inventory reservation smoke test failed.'
  for status in 1 2 3 4; do curl -fsS -X PATCH -H "Authorization: Bearer $employee_token" -H 'Content-Type: application/json' -d "{\"status\":$status}" "$base/api/orders/$order_id/status" >/dev/null; done
  local status_history_count
  status_history_count="$(curl -fsS -H "Authorization: Bearer $employee_token" "$base/api/orders/$order_id/history" | grep -o '"status":' | wc -l | tr -d ' ')"
  [[ "$status_history_count" == 5 ]] || fail 'Order status history smoke test failed.'
  cancelled_json="$(curl -fsS -H "Authorization: Bearer $employee_token" -H 'Content-Type: application/json' -d "{\"customerId\":\"$customer_id\",\"items\":[{\"productId\":\"$product_id\",\"quantity\":3}]}" "$base/api/orders")"
  cancelled_id="$(sed -n 's/.*"id":"\([^"]*\)".*/\1/p' <<<"$cancelled_json")"
  curl -fsS -X PATCH -H "Authorization: Bearer $employee_token" -H 'Content-Type: application/json' -d '{"status":5}' "$base/api/orders/$cancelled_id/status" >/dev/null
  available="$(curl -fsS -H "Authorization: Bearer $employee_token" "$base/api/products/$product_id" | sed -n 's/.*"availableStock":\([0-9]*\).*/\1/p')"
  [[ "$available" == 13 ]] || fail 'Cancellation did not release the reserved inventory.'
  history_count="$(curl -fsS -H "Authorization: Bearer $employee_token" "$base/api/inventory/products/$product_id/transactions" | sed -n 's/.*"totalCount":\([0-9]*\).*/\1/p')"
  [[ "$history_count" -ge 4 ]] || fail 'Inventory history smoke test failed.'
  analytics_sales="$(curl -fsS -H "Authorization: Bearer $admin_token" "$base/api/analytics/overview" | sed -n 's/.*"sales":\([0-9.]*\).*/\1/p')"
  [[ -n "$analytics_sales" ]] || fail 'Sales analytics smoke test failed.'
  printf 'API smoke checks passed: registration/login, customers/products, receipt, order pricing/reservation, status flow/history, cancellation/release, ledger, analytics.\n'
}
start_app() {
  require_docker
  ensure_env
  compose config --quiet || fail 'Compose configuration validation failed.'
  local api_port pg_port running_orderflow
  api_port="$(sed -n 's/^ORDERFLOW_API_PORT=//p' "$ENV_FILE" | head -n1)"
  pg_port="$(sed -n 's/^POSTGRES_PORT=//p' "$ENV_FILE" | head -n1)"
  api_port="${api_port:-8080}"
  pg_port="${pg_port:-5432}"
  [[ "$api_port" =~ ^[0-9]+$ && "$api_port" -ge 1 && "$api_port" -le 65535 ]] || fail 'ORDERFLOW_API_PORT must be an integer from 1 to 65535.'
  [[ "$pg_port" =~ ^[0-9]+$ && "$pg_port" -ge 1 && "$pg_port" -le 65535 ]] || fail 'POSTGRES_PORT must be an integer from 1 to 65535.'
  running_orderflow="$(docker ps --filter label=com.docker.compose.project=orderflow --format '{{.Label "com.docker.compose.project"}}' | head -n1)"
  existing_containers="$(docker ps -a --filter label=com.docker.compose.project=orderflow --format '{{.Names}}')"
  [[ -z "$existing_containers" ]] || printf 'Detected existing OrderFlow containers: %s\n' "$(tr '\n' ' ' <<<"$existing_containers")"
  existing_volume="$(docker volume ls --filter name=orderflow_postgres-data --format '{{.Name}}' | head -n1)"
  [[ -z "$existing_volume" ]] || printf "Existing PostgreSQL volume '%s' will be preserved and reused.\n" "$existing_volume"
  if command -v ss >/dev/null 2>&1; then
    for port in "$api_port" "$pg_port"; do
      if ss -ltnH | awk -v suffix=":$port" '$4 ~ (suffix "$" ) { found=1 } END { exit !found }'; then
        [[ "$running_orderflow" == orderflow ]] || fail "Port $port is already in use. Change its value in .env or stop the other service."
      fi
    done
  fi
  printf 'Building OrderFlow API image...\n'
  compose build api
  compose up -d db
  wait_db
  printf 'Starting OrderFlow API...\n'
  compose up -d api
  local port
  port="$api_port"
  local healthy=0
  for _ in $(seq 1 36); do
    if curl --fail --silent --max-time 3 "http://localhost:$port/health" | grep -q Healthy; then
      healthy=1
      break
    fi
    sleep 5
  done
  if (( healthy == 0 )); then compose logs --tail 100 db api; fail 'OrderFlow API did not become healthy within 180 seconds. Review the logs above.'; fi
  local migration_count
  migration_count="$(compose exec -T db psql -U orderflow -d orderflow -Atc 'SELECT count(*) FROM "__EFMigrationsHistory";')"
  [[ "$migration_count" -ge 1 ]] || fail 'EF Core migration history verification failed.'
  api_smoke "$port"
  printf '\nOrderFlow is healthy.\nAPI: http://localhost:%s\nSwagger: http://localhost:%s/swagger\nHealth: http://localhost:%s/health\n' "$port" "$port" "$port"
}

case "$ACTION" in
  Start) start_app ;;
  Stop) require_docker; if [[ -f "$ENV_FILE" ]]; then compose stop; else docker compose --project-directory "$ROOT" --project-name orderflow -f "$COMPOSE" stop; fi; printf 'Stopped services. PostgreSQL volume was preserved.\n' ;;
  Restart)
    require_docker
    before=-1
    if [[ -f "$ENV_FILE" ]]; then
      if [[ -n "$(compose ps -q db 2>/dev/null)" ]]; then before="$(compose exec -T db psql -U orderflow -d orderflow -Atc 'SELECT count(*) FROM "Orders";' 2>/dev/null || printf '%s' -1)"; fi
      compose stop
    fi
    start_app
    if (( before >= 0 )); then
      after="$(compose exec -T db psql -U orderflow -d orderflow -Atc 'SELECT count(*) FROM "Orders";')"
      (( after >= before )) || fail 'Database persistence verification failed after restart.'
      printf 'Database persistence verified: order count remained at least %s after restart.\n' "$before"
    fi
    ;;
  Status) require_docker; if [[ -f "$ENV_FILE" ]]; then compose ps; else printf 'No .env configuration yet.\n'; fi ;;
  Logs) require_docker; [[ -f "$ENV_FILE" ]] || fail 'No .env configuration yet.'; compose logs --tail 100 -f ;;
  Test)
    if command -v dotnet >/dev/null 2>&1; then
      (cd "$ROOT" && dotnet restore OrderFlow.slnx && dotnet build OrderFlow.slnx --configuration Release --no-restore && dotnet test OrderFlow.slnx --configuration Release --no-build --logger 'trx;LogFileName=OrderFlow.trx' --results-directory TestResults)
    else
      require_docker
      docker run --rm -v "$ROOT:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet restore OrderFlow.slnx && \
      docker run --rm -v "$ROOT:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build OrderFlow.slnx --configuration Release --no-restore && \
      docker run --rm -v "$ROOT:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test OrderFlow.slnx --configuration Release --no-build --logger 'trx;LogFileName=OrderFlow.trx' --results-directory TestResults
    fi
    ;;
  *) fail 'Usage: ./RUNLOCALLY.sh [Start|Stop|Restart|Status|Logs|Test]' ;;
esac
