using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application;
namespace OrderFlow.API.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(IAuthService auth, IValidator<RegisterRequest> validator) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));

        return Ok(await auth.RegisterAsync(request, ct));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var token = await auth.LoginAsync(request, ct);
        return token is null ? Unauthorized() : Ok(token);
    }
}
