using BionicPRO.Auth.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using BionicPRO.Auth.Models;

namespace BionicPRO.Auth.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Login attempt for username: {Username}", request.Username);
        var existingSessionId = HttpContext.Session.GetString("sessionId");

        if (!string.IsNullOrEmpty(existingSessionId))
        {
            _logger.LogInformation("Existing session found for user {Username}, sessionId: {SessionId}", request.Username, existingSessionId);
        }

        var (success, sessionId, error) = await _authService.AuthenticateAsync(
            request.Username,
            request.Password,
            existingSessionId
        );

        if (!success || sessionId == null)
        {
            _logger.LogWarning("Authentication failed for username: {Username}. Error: {Error}", request.Username, error);
            return Unauthorized(new { error });
        }

        _logger.LogInformation("Authentication successful for username: {Username}, sessionId: {SessionId}", request.Username, sessionId);

        // Сохраняем sessionId в встроенную сессию ASP.NET Core
        HttpContext.Session.SetString("sessionId", sessionId);

        var claims = new Claim[]
        {
            new(ClaimTypes.Name, request.Username),
            new("session_id", sessionId)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
        });

        _logger.LogInformation("Session set for username: {Username}, sessionId: {SessionId}, expires in 24 hours", request.Username, sessionId);
        return Ok(new { message = "Authenticated successfully", username = request.Username });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var sessionId = HttpContext.Session.GetString("sessionId");
        var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        _logger.LogInformation("Logout attempt for username: {Username}, sessionId: {SessionId}", username, sessionId);

        if (!string.IsNullOrEmpty(sessionId))
        {
            await _authService.LogoutAsync(sessionId);
            HttpContext.Session.Remove("sessionId");
            _logger.LogInformation("Session terminated for username: {Username}, sessionId: {SessionId}", username, sessionId);
        }
        else
        {
            _logger.LogWarning("Logout attempt but no session found for user: {Username}", username);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("User signed out: {Username}", username);
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpGet("me")]
    //[Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var sessionId = HttpContext.Session.GetString("sessionId");
        _logger.LogInformation("GetCurrentUser request, sessionId: {SessionId}", sessionId ?? "none");

        if (string.IsNullOrEmpty(sessionId))
        {
            _logger.LogWarning("GetCurrentUser: No session found");
            return Unauthorized();
        }

        var (isValid, session) = await _authService.GetValidSessionAsync(sessionId);

        if (!isValid || session == null)
        {
            _logger.LogWarning("GetCurrentUser: Invalid or expired session for sessionId: {SessionId}", sessionId);
            return Unauthorized();
        }

        _logger.LogInformation("GetCurrentUser: Valid session found for username: {Username}, token expires at: {ExpiresAt}", session.Username, session.AccessTokenExpiresAt);
        return Ok(new
        {
            session.Username,
            session.AccessTokenExpiresAt
        });
    }
}