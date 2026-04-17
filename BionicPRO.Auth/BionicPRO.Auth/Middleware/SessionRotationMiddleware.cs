using BionicPRO.Auth.Services;
using Microsoft.AspNetCore.Http;

namespace BionicPRO.Auth.Middleware;

public class SessionRotationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionRotationMiddleware> _logger;

    public SessionRotationMiddleware(RequestDelegate next, ILogger<SessionRotationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuthService authService)
    {
        var sessionId = context.Session.GetString("sessionId");
        _logger.LogInformation("SessionRotationMiddleware processing request: {Method} {Path}", context.Request.Method, context.Request.Path);

        if (!string.IsNullOrEmpty(sessionId))
        {
            _logger.LogInformation("Session found: {SessionId}", sessionId);

            var shouldRotate = context.Request.Path.StartsWithSegments("/api/reports") ||
                               context.Request.Path.StartsWithSegments("/api/auth/me");

            if (shouldRotate)
            {
                _logger.LogInformation("Session rotation triggered for path: {Path}, sessionId: {SessionId}", context.Request.Path, sessionId);
                var newSession = await authService.RotateSessionAsync(sessionId);

                if (newSession != null)
                {
                    context.Session.SetString("sessionId", newSession.SessionId);
                    _logger.LogInformation("Session rotated successfully from {OldSessionId} to {NewSessionId}",
                        sessionId, newSession.SessionId);
                }
                else
                {
                    _logger.LogWarning("Session rotation failed for sessionId: {SessionId}", sessionId);
                }
            }
            else
            {
                _logger.LogInformation("Session rotation not needed for path: {Path}", context.Request.Path);
            }
        }
        else
        {
            _logger.LogInformation("No session found for request: {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        await _next(context);
    }
}