using System.Text.Json;
using BionicPRO.Auth.Models;
using BionicPRO.Auth.Services;

public class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthService> _logger;
    private readonly ITokenStore _tokenStore;

    public AuthService(
        ITokenStore tokenStore,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _tokenStore = tokenStore;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(bool success, string? sessionId, string? error)> AuthenticateAsync(
        string username,
        string password,
        string? existingSessionId = null)
    {
        _logger.LogInformation("Starting authentication for username: {Username}", username);
        try
        {
            _logger.LogInformation("Requesting token from Keycloak for user: {Username}", username);
            var tokenClient = _httpClientFactory.CreateClient();
            var tokenRequest = new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = _configuration["Keycloak:ClientId"]!,
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid profile email"
            };

            var response = await tokenClient.PostAsync(
                $"{_configuration["Keycloak:Authority"]}/protocol/openid-connect/token",
                new FormUrlEncodedContent(tokenRequest)
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token request failed for username: {Username}, status code: {StatusCode}", username, response.StatusCode);
                return (false, null, "Invalid credentials");
            }

            var tokenJson = await response.Content.ReadAsStringAsync();
            var tokens = JsonSerializer.Deserialize<TokenResponse>(tokenJson);

            if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                _logger.LogWarning("Failed to deserialize token response for username: {Username}", username);
                return (false, null, "Failed to get tokens");
            }

            var sessionId = existingSessionId ?? GenerateSessionId();
            _logger.LogInformation("Generated sessionId: {SessionId} for user: {Username}", sessionId, username);

            var session = new SessionData
            {
                SessionId = sessionId,
                Username = username,
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn),
                RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.RefreshExpiresIn),
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };

            await _tokenStore.SaveSessionAsync(session);
            _logger.LogInformation("Session saved successfully for user: {Username}, expires in {ExpiresIn} seconds", username, tokens.ExpiresIn);

            return (true, sessionId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error for username: {Username}", username);
            return (false, null, "Authentication failed");
        }
    }

    public async Task<(bool success, string? sessionId, string? error)> RefreshTokenAsync(string refreshToken)
    {
        _logger.LogInformation("Starting token refresh with refresh token");
        try
        {
            var oldSession = await _tokenStore.GetSessionByRefreshTokenAsync(refreshToken);
            if (oldSession == null)
            {
                _logger.LogWarning("Session not found for provided refresh token");
                return (false, null, "Invalid refresh token");
            }

            _logger.LogInformation("Session found for user: {Username}, sessionId: {SessionId}", oldSession.Username, oldSession.SessionId);

            var tokenClient = _httpClientFactory.CreateClient();
            var tokenRequest = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _configuration["Keycloak:ClientId"]!,
                ["refresh_token"] = refreshToken
            };

            var response = await tokenClient.PostAsync(
                $"{_configuration["Keycloak:Authority"]}/protocol/openid-connect/token",
                new FormUrlEncodedContent(tokenRequest)
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token refresh failed for user: {Username}, status code: {StatusCode}", oldSession.Username, response.StatusCode);
                await _tokenStore.RemoveSessionAsync(oldSession.SessionId);
                return (false, null, "Refresh token expired");
            }

            var tokenJson = await response.Content.ReadAsStringAsync();
            var tokens = JsonSerializer.Deserialize<TokenResponse>(tokenJson);

            if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                _logger.LogWarning("Failed to deserialize token response for user: {Username}", oldSession.Username);
                return (false, null, "Failed to refresh tokens");
            }

            oldSession.AccessToken = tokens.AccessToken;
            oldSession.RefreshToken = tokens.RefreshToken;
            oldSession.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
            oldSession.RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.RefreshExpiresIn);
            oldSession.LastAccessedAt = DateTime.UtcNow;

            await _tokenStore.UpdateSessionAsync(oldSession);
            _logger.LogInformation("Token refreshed successfully for user: {Username}, sessionId: {SessionId}", oldSession.Username, oldSession.SessionId);

            return (true, oldSession.SessionId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh error");
            return (false, null, "Refresh failed");
        }
    }

    public async Task<bool> LogoutAsync(string sessionId)
    {
        _logger.LogInformation("Logout initiated for sessionId: {SessionId}", sessionId);
        var session = await _tokenStore.GetSessionAsync(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for logout, sessionId: {SessionId}", sessionId);
            return false;
        }

        await _tokenStore.RemoveSessionAsync(sessionId);
        _logger.LogInformation("Session removed successfully for username: {Username}, sessionId: {SessionId}", session.Username, sessionId);
        return true;
    }

    public async Task<bool> ValidateSessionAsync(string sessionId)
    {
        _logger.LogInformation("Validating session: {SessionId}", sessionId);
        var session = await _tokenStore.GetSessionAsync(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for validation, sessionId: {SessionId}", sessionId);
            return false;
        }

        _logger.LogInformation("Session found for user: {Username}, checking token validity", session.Username);

        if (!session.IsAccessTokenValid)
        {
            _logger.LogInformation("Access token is invalid for user: {Username}, attempting refresh", session.Username);
            if (session.IsRefreshTokenValid)
            {
                var (success, _, _) = await RefreshTokenAsync(session.RefreshToken);
                if (success)
                {
                    _logger.LogInformation("Token refresh successful for user: {Username}", session.Username);
                    return true;
                }
                _logger.LogWarning("Token refresh failed for user: {Username}", session.Username);
            }
            else
            {
                _logger.LogWarning("Both access token and refresh token are invalid for user: {Username}", session.Username);
            }

            await _tokenStore.RemoveSessionAsync(sessionId);
            return false;
        }

        _logger.LogInformation("Access token is valid for user: {Username}, updating last accessed time", session.Username);
        session.LastAccessedAt = DateTime.UtcNow;
        await _tokenStore.UpdateSessionAsync(session);
        return true;
    }

    public async Task<(bool isValid, SessionData? session)> GetValidSessionAsync(string sessionId)
    {
        _logger.LogInformation("Getting valid session for sessionId: {SessionId}", sessionId);
        var session = await _tokenStore.GetSessionAsync(sessionId);

        if (session == null)
        {
            _logger.LogWarning("Session not found, sessionId: {SessionId}", sessionId);
            return (false, null);
        }

        _logger.LogInformation("Session found for user: {Username}", session.Username);

        if (!session.IsAccessTokenValid && session.IsRefreshTokenValid)
        {
            _logger.LogInformation("Access token expired for user: {Username}, attempting token refresh", session.Username);
            var (success, newSessionId, _) = await RefreshTokenAsync(session.RefreshToken);
            if (success && newSessionId != null)
            {
                _logger.LogInformation("Token refresh successful for user: {Username}", session.Username);
                session = await _tokenStore.GetSessionAsync(newSessionId);
                return (session != null, session);
            }

            _logger.LogWarning("Token refresh failed for user: {Username}", session.Username);
            return (false, null);
        }

        if (!session.IsAccessTokenValid)
        {
            _logger.LogWarning("Access token expired and refresh token also expired for user: {Username}", session.Username);
            return (false, null);
        }

        _logger.LogInformation("Session is valid for user: {Username}, token expires at: {ExpiresAt}", session.Username, session.AccessTokenExpiresAt);
        return (true, session);
    }

    public async Task<SessionData?> RotateSessionAsync(string oldSessionId)
    {
        _logger.LogInformation("Starting session rotation for old sessionId: {OldSessionId}", oldSessionId);
        var oldSession = await _tokenStore.GetSessionAsync(oldSessionId);
        if (oldSession == null)
        {
            _logger.LogWarning("Session not found for rotation, sessionId: {OldSessionId}", oldSessionId);
            return null;
        }

        var newSessionId = GenerateSessionId();
        _logger.LogInformation("Session rotation for user: {Username}, old sessionId: {OldSessionId}, new sessionId: {NewSessionId}", oldSession.Username, oldSessionId, newSessionId);

        var newSession = new SessionData
        {
            SessionId = newSessionId,
            Username = oldSession.Username,
            AccessToken = oldSession.AccessToken,
            RefreshToken = oldSession.RefreshToken,
            AccessTokenExpiresAt = oldSession.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = oldSession.RefreshTokenExpiresAt,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };

        await _tokenStore.RemoveSessionAsync(oldSessionId);
        await _tokenStore.SaveSessionAsync(newSession);
        _logger.LogInformation("Session rotation completed successfully for user: {Username}", oldSession.Username);

        return newSession;
    }

    public async Task<bool> HasAccessToReportAsync(string sessionId, string reportUserId)
    {
        _logger.LogInformation("Checking access for sessionId: {SessionId}, reportUserId: {ReportUserId}", sessionId, reportUserId);
        var session = await _tokenStore.GetSessionAsync(sessionId);

        if (session == null)
        {
            _logger.LogWarning("Session not found for access check, sessionId: {SessionId}", sessionId);
            return false;
        }
        
        var hasAccess = session.UserId == reportUserId;
        _logger.LogInformation("Access check for user: {Username}, sessionUserId: {SessionUserId}, reportUserId: {ReportUserId}, access: {HasAccess}", 
            session.Username, session.UserId, reportUserId, hasAccess);

        return hasAccess;
    }

    private static string GenerateSessionId()
    {
        return Guid.NewGuid().ToString("N");
    }
}