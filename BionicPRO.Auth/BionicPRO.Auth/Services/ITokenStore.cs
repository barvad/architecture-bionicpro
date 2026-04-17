using BionicPRO.Auth.Models;

namespace BionicPRO.Auth.Services;

public interface ITokenStore
{
    Task<SessionData?> GetSessionAsync(string sessionId);
    Task SaveSessionAsync(SessionData session);
    Task RemoveSessionAsync(string sessionId);
    Task<bool> SessionExistsAsync(string sessionId);
    Task<SessionData?> GetSessionByRefreshTokenAsync(string refreshToken);
    Task UpdateSessionAsync(SessionData session);
}