using BionicPRO.Auth.Services;
using BionicPRO.Auth.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

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

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, request.Username),
            new("session_id", sessionId)
        };

        // Извлекаем все claims из access token
        var session = await _authService.GetValidSessionAsync(sessionId);
        if (session.session != null)
        {
            var tokenClaims = ExtractClaimsFromToken(session.session.AccessToken);
            foreach (var claim in tokenClaims)
            {
                claims.Add(claim);
            }
            _logger.LogInformation("Extracted {ClaimCount} claims for user {Username}", tokenClaims.Count, request.Username);
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var hasOtp = principal
            .FindAll("amr")
            .Any(c => c.Value == "otp");

        if (!hasOtp)
        {
            return Forbid("MFA required");
        }
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

    [HttpGet("me/claims")]
    [Authorize]
    public IActionResult GetCurrentUserClaims()
    {
        var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";
        _logger.LogInformation("GetCurrentUserClaims request for user: {Username}", username);

        var allClaims = User.Claims
            .GroupBy(c => c.Type)
            .Select(g => new
            {
                Type = g.Key,
                Values = g.Select(c => c.Value).ToList()
            })
            .ToList();

        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        _logger.LogInformation("User {Username} has {ClaimCount} claim types and {RoleCount} roles", username, allClaims.Count, roles.Count);

        return Ok(new
        {
            username,
            claimsCount = allClaims.Count,
            rolesCount = roles.Count,
            roles,
            allClaims
        });
    }

    private static List<Claim> ExtractClaimsFromToken(string accessToken)
    {
        var claims = new List<Claim>();
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(accessToken);

            // Копируем стандартные claims из токена
            var claimsToSkip = new[] { "aud", "iat", "exp", "nbf", "jti" }; // Технические claims

            foreach (var claim in token.Claims)
            {
                // Пропускаем технические claims
                if (claimsToSkip.Contains(claim.Type))
                    continue;

                // Обрабатываем специальные claims
                if (claim.Type == "realm_access" || claim.Type == "resource_access")
                {
                    ProcessAccessClaims(claim, claims);
                }
                else
                {
                    // Добавляем обычные claims
                    claims.Add(new Claim(claim.Type, claim.Value));
                }
            }

            return claims;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting claims from token: {ex.Message}");
            return new List<Claim>();
        }
    }

    private static void ProcessAccessClaims(System.Security.Claims.Claim accessClaim, List<Claim> claims)
    {
        try
        {
            using (var document = System.Text.Json.JsonDocument.Parse(accessClaim.Value))
            {
                var root = document.RootElement;

                if (accessClaim.Type == "realm_access")
                {
                    // Извлекаем realm roles
                    if (root.TryGetProperty("roles", out var rolesElement))
                    {
                        foreach (var role in rolesElement.EnumerateArray())
                        {
                            if (role.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                var roleValue = role.GetString();
                                if (!string.IsNullOrEmpty(roleValue))
                                {
                                    claims.Add(new Claim(ClaimTypes.Role, roleValue));
                                }
                            }
                        }
                    }
                }
                else if (accessClaim.Type == "resource_access")
                {
                    // Извлекаем client/resource roles
                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.Value.TryGetProperty("roles", out var rolesElement))
                        {
                            foreach (var role in rolesElement.EnumerateArray())
                            {
                                if (role.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    var roleValue = role.GetString();
                                    if (!string.IsNullOrEmpty(roleValue))
                                    {
                                        claims.Add(new Claim(ClaimTypes.Role, $"{property.Name}:{roleValue}"));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing access claims: {ex.Message}");
        }
    }

    private static List<string> ExtractRolesFromToken(string accessToken)
    {
        var roles = new List<string>();
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(accessToken);

            // Keycloak stores roles in multiple possible locations
            // 1. realm_access.roles (realm roles)
            var realmAccessClaim = token.Claims.FirstOrDefault(c => c.Type == "realm_access");
            if (realmAccessClaim != null)
            {
                using (var document = System.Text.Json.JsonDocument.Parse(realmAccessClaim.Value))
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("roles", out var rolesElement))
                    {
                        foreach (var role in rolesElement.EnumerateArray())
                        {
                            if (role.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                roles.Add(role.GetString() ?? string.Empty);
                            }
                        }
                    }
                }
            }

            // 2. resource_access.[clientId].roles (client roles)
            var resourceAccessClaim = token.Claims.FirstOrDefault(c => c.Type == "resource_access");
            if (resourceAccessClaim != null)
            {
                using (var document = System.Text.Json.JsonDocument.Parse(resourceAccessClaim.Value))
                {
                    var root = document.RootElement;
                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.Value.TryGetProperty("roles", out var rolesElement))
                        {
                            foreach (var role in rolesElement.EnumerateArray())
                            {
                                if (role.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    roles.Add(role.GetString() ?? string.Empty);
                                }
                            }
                        }
                    }
                }
            }

            return roles;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting roles from token: {ex.Message}");
            return new List<string>();
        }
    }
}