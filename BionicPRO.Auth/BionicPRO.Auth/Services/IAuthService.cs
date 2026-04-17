using BionicPRO.Auth.Models;

namespace BionicPRO.Auth.Services;

public interface IAuthService
{
    Task<(bool success, string? sessionId, string? error)> AuthenticateAsync(
        string username,
        string password,
        string? existingSessionId = null);

    Task<(bool success, string? sessionId, string? error)> RefreshTokenAsync(string refreshToken);

    Task<bool> LogoutAsync(string sessionId);

    Task<bool> ValidateSessionAsync(string sessionId);

    Task<(bool isValid, SessionData? session)> GetValidSessionAsync(string sessionId);

    Task<SessionData?> RotateSessionAsync(string oldSessionId);

    Task<bool> HasAccessToReportAsync(string sessionId, string reportUserId);
}