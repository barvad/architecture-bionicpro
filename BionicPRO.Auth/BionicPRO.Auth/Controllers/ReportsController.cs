using BionicPRO.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BionicPRO.Auth.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IAuthService authService,
        IHttpClientFactory httpClientFactory,
        ILogger<ReportsController> logger)
    {
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetReports([FromQuery] string? userId = null)
    {
        var sessionId = HttpContext.Session.GetString("sessionId");
        _logger.LogInformation("GetReports request initiated, sessionId: {SessionId}, userId: {UserId}", sessionId ?? "none", userId ?? "none");

        if (string.IsNullOrEmpty(sessionId))
        {
            _logger.LogWarning("GetReports: No session found");
            return Unauthorized();
        }

        var (isValid, session) = await _authService.GetValidSessionAsync(sessionId);

        if (!isValid || session == null)
        {
            _logger.LogWarning("GetReports: Invalid or expired session for sessionId: {SessionId}", sessionId);
            return Unauthorized();
        }

        var targetUserId = userId ?? session.UserId;
        _logger.LogInformation("GetReports: Session valid for user: {Username}, requesting reports for targetUserId: {TargetUserId}", session.Username, targetUserId);

        if (!await _authService.HasAccessToReportAsync(sessionId, targetUserId))
        {
            _logger.LogWarning("GetReports: Access denied for user: {Username}, targetUserId: {TargetUserId}", session.Username, targetUserId);
            return Forbid();
        }

        _logger.LogInformation("GetReports: Access granted for user: {Username}, fetching reports from backend", session.Username);
        var reports="{ \"reports\": [ { \"id\": 1, \"title\": \"Report 1\", \"content\": \"Content of report 1\" }, { \"id\": 2, \"title\": \"Report 2\", \"content\": \"Content of report 2\" } ] }";
        return Content(reports, "application/json");
    }
}