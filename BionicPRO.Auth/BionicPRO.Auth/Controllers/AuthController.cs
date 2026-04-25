
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BionicPRO.Auth.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;

    public AuthController(ILogger<AuthController> logger)
    {
        _logger = logger;
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = "/")
    {
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = returnUrl
        }, OpenIdConnectDefaults.AuthenticationScheme);
    }


    [HttpGet("logout")]
    [Authorize]
    public IActionResult Logout([FromQuery] string? postLogoutRedirectUri = "/")
    {
        _logger.LogInformation("Logout requested for user: {User}", User.Identity?.Name);
        var props = new AuthenticationProperties { RedirectUri = postLogoutRedirectUri ?? "/" };
        return SignOut(props, CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult GetCurrentUser()
    {
        var username = User.Identity?.Name ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        _logger.LogInformation("GetCurrentUser for {Username}", username);

        var exp = User.FindFirst("exp")?.Value;

        return Ok(new
        {
            username,
            expires = exp
        });
    }

    [HttpGet("me/claims")]
    [Authorize]
    public IActionResult GetCurrentUserClaims()
    {
        var username = User.Identity?.Name ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        _logger.LogInformation("GetCurrentUserClaims request for user: {Username}", username);

        var allClaims = User.Claims
            .GroupBy(c => c.Type)
            .Select(g => new
            {
                Type = g.Key,
                Values = g.Select(c => c.Value).ToList()
            })
            .ToList();

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        return Ok(new
        {
            username,
            claimsCount = allClaims.Count,
            rolesCount = roles.Count,
            roles,
            allClaims
        });
    }


    [HttpGet("/signout-callback-oidc")]
    public async Task<IActionResult> SignoutCallback()
    {
        var username = User.Identity?.Name ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        _logger.LogInformation("Signout callback invoked for user: {Username}", username);

        try
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error signing out local cookie during signout callback");
        }

        var redirectUrl = "/";
        return Redirect(redirectUrl);
    }
}
