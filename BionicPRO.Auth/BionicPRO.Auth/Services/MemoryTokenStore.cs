using BionicPRO.Auth.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BionicPRO.Auth.Services;

public class MemoryTokenStore : ITokenStore
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryTokenStore> _logger;

    public MemoryTokenStore(IMemoryCache cache, ILogger<MemoryTokenStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<SessionData?> GetSessionAsync(string sessionId)
    {
        var found = _cache.TryGetValue(sessionId, out SessionData? session);
        _logger.LogInformation("MemoryTokenStore.GetSessionAsync: sessionId={SessionId}, found={Found}", sessionId, found);
        if (found && session != null)
        {
            _logger.LogInformation("MemoryTokenStore.GetSessionAsync: username={Username}, accessExpires={AccessExpires}, refreshExpires={RefreshExpires}",
                session.Username, session.AccessTokenExpiresAt, session.RefreshTokenExpiresAt);
        }
        return Task.FromResult(session);
    }

    public Task SaveSessionAsync(SessionData session)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = session.RefreshTokenExpiresAt,
            SlidingExpiration = TimeSpan.FromHours(1)
        };

        _cache.Set(session.SessionId, session, options);
        _logger.LogInformation("MemoryTokenStore.SaveSessionAsync: saved sessionId={SessionId}, username={Username}, accessExpires={AccessExpires}, refreshExpires={RefreshExpires}",
            session.SessionId, session.Username, session.AccessTokenExpiresAt, session.RefreshTokenExpiresAt);

        if (!string.IsNullOrEmpty(session.RefreshToken))
        {
            _cache.Set($"rt:{session.RefreshToken}", session.SessionId, options);
            _logger.LogInformation("MemoryTokenStore.SaveSessionAsync: saved refresh-token mapping rt:{RefreshToken} -> sessionId={SessionId}", 
                session.RefreshToken, session.SessionId);
        }

        return Task.CompletedTask;
    }

    public Task RemoveSessionAsync(string sessionId)
    {
        var session = _cache.Get<SessionData>(sessionId);
        if (session != null && !string.IsNullOrEmpty(session.RefreshToken))
        {
            _cache.Remove($"rt:{session.RefreshToken}");
            _logger.LogInformation("MemoryTokenStore.RemoveSessionAsync: removed refresh-token-key rt:{RefreshToken}", session.RefreshToken);
        }

        _cache.Remove(sessionId);
        _logger.LogInformation("MemoryTokenStore.RemoveSessionAsync: removed sessionId={SessionId}", sessionId);
        return Task.CompletedTask;
    }

    public Task<bool> SessionExistsAsync(string sessionId)
    {
        return Task.FromResult(_cache.TryGetValue(sessionId, out _));
    }

    public Task<SessionData?> GetSessionByRefreshTokenAsync(string refreshToken)
    {
        if (_cache.TryGetValue($"rt:{refreshToken}", out string? sessionId) && sessionId != null)
        {
            _logger.LogInformation("MemoryTokenStore.GetSessionByRefreshTokenAsync: found sessionId={SessionId} for refresh token", sessionId);
            return GetSessionAsync(sessionId);
        }

        _logger.LogInformation("MemoryTokenStore.GetSessionByRefreshTokenAsync: no session found for refresh token");
        return Task.FromResult<SessionData?>(null);
    }

    public Task UpdateSessionAsync(SessionData session)
    {
        return SaveSessionAsync(session);
    }
}

