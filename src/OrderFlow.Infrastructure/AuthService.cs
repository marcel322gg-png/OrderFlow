using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using OrderFlow.Application;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
namespace OrderFlow.Infrastructure;

public sealed class AuthService(UserManager<IdentityUserEntity> userManager, IConfiguration configuration) : IAuthService
{
    public async Task<TokenDto> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var user = new IdentityUserEntity { UserName = request.Email, Email = request.Email };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            throw new ArgumentException(string.Join("; ", createResult.Errors.Select(error => error.Description)));

        var roleResult = await userManager.AddToRoleAsync(user, "Employee");
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            throw new InvalidOperationException("Unable to assign the Employee role.");
        }

        return await IssueTokenAsync(user);
    }

    public async Task<TokenDto?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password)) return null;
        return await IssueTokenAsync(user);
    }

    private async Task<TokenDto> IssueTokenAsync(IdentityUserEntity user)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey must be configured.");
        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer must be configured.");
        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience must be configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var minutes = int.TryParse(configuration["Jwt:ExpiryMinutes"], out var configuredMinutes)
            ? Math.Clamp(configuredMinutes, 1, 1440)
            : 30;
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(minutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(ClaimTypes.NameIdentifier, user.Id.ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var token = new JwtSecurityToken(issuer, audience, claims, expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new TokenDto(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc,
            user.Email!, roles);
    }
}
