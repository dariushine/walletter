using Walletter.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _service;

    public AuthController(AuthService service)
    {
        _service = service;
    }

    [AllowAnonymous]
    [HttpGet("status")]
    public IActionResult Status()
        => Ok(new { enabled = _service.AuthEnabled });

    [Authorize]
    [HttpGet("session")]
    public IActionResult Session()
        => Ok(new
        {
            authenticated = true,
            disabled = !_service.AuthEnabled,
        });

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand cmd, CancellationToken ct)
    {
        var result = await _service.Login(cmd.Username, cmd.Password, cmd.Remember, ct);

        Response.Cookies.Append("access_token", result.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            MaxAge = TimeSpan.FromMinutes(15),
        });
        Response.Cookies.Append("refresh_token", result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            MaxAge = TimeSpan.FromDays(30),
        });

        return Ok(new
        {
            accessToken = result.AccessToken,
            tokenType = result.TokenType,
            expiresIn = result.ExpiresIn,
            remember = result.Remember,
        });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("access_token");
        Response.Cookies.Delete("refresh_token");
        return Ok(new { success = true });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies["refresh_token"];
        var result = await _service.Refresh(refreshToken, ct);

        Response.Cookies.Append("access_token", result.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            MaxAge = TimeSpan.FromMinutes(15),
        });

        return Ok(new { accessToken = result.AccessToken, tokenType = result.TokenType });
    }

    [Authorize]
    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
        => Ok(await _service.ListSessions(ct));

    [Authorize]
    [HttpDelete("sessions/{jti}")]
    public async Task<IActionResult> RevokeSession(string jti, CancellationToken ct)
        => Ok(await _service.RevokeSession(jti, ct));

    [Authorize]
    [HttpGet("tokens")]
    public async Task<IActionResult> ListTokens(CancellationToken ct)
        => Ok(await _service.ListTokens(ct));

    [Authorize]
    [HttpPost("tokens")]
    public async Task<IActionResult> CreateToken([FromBody] CreateTokenCommand cmd, CancellationToken ct)
        => Ok(await _service.CreateToken(cmd.Name, ct));

    [Authorize]
    [HttpDelete("tokens/{id:int}")]
    public async Task<IActionResult> RevokeToken(int id, CancellationToken ct)
        => Ok(await _service.RevokeToken(id, ct));
}
