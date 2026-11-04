using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OrderFlow.Application;
using OrderFlow.API;
using OrderFlow.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, logger) => logger.ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "RalseiFlow API",
    Version = "v1",
    Description = "Orders, stock & business — in one place."
}));
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Default.");
builder.Services.AddDbContext<OrderFlowDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddIdentityCore<IdentityUserEntity>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<OrderFlowDbContext>();

var key = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Configure Jwt:SigningKey with a secret of at least 32 characters.");
var issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Configure Jwt:Issuer.");
var audience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Configure Jwt:Audience.");
if (Encoding.UTF8.GetByteCount(key) < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderValidator>();
var app = builder.Build();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "RalseiFlow API v1"));
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", async (OrderFlowDbContext db, CancellationToken ct) =>
{
    try
    {
        return await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "Healthy" })
            : Results.Problem("Database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception) when (!ct.IsCancellationRequested)
    {
        return Results.Problem("Database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).WithTags("System");
app.MapControllers();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
    await db.Database.MigrateAsync();
}
await EnsureRolesAsync(app.Services);
await EnsureBootstrapAdminAsync(app.Services, app.Configuration);
app.Run();

static async Task EnsureRolesAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var name in new[] { "Admin", "Employee" })
    {
        if (await roleManager.RoleExistsAsync(name)) continue;
        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(name));
        if (!result.Succeeded)
            throw new InvalidOperationException($"Unable to initialize role '{name}'.");
    }
}

static async Task EnsureBootstrapAdminAsync(IServiceProvider services, IConfiguration configuration)
{
    var email = configuration["BootstrapAdmin:Email"];
    var password = configuration["BootstrapAdmin:Password"];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

    using var scope = services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUserEntity>>();
    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        user = new IdentityUserEntity { UserName = email, Email = email, EmailConfirmed = true };
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
            throw new InvalidOperationException("Unable to create bootstrap administrator: " +
                string.Join("; ", createResult.Errors.Select(error => error.Description)));
    }

    if (await userManager.IsInRoleAsync(user, "Admin")) return;
    var roleResult = await userManager.AddToRoleAsync(user, "Admin");
    if (!roleResult.Succeeded)
        throw new InvalidOperationException("Unable to assign bootstrap administrator role.");
}
public partial class Program { }
