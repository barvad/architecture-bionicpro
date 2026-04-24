using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace BionicPRO.Auth.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<ReportsController> _logger;
    private const int RequestTimeoutSeconds = 30;

    public ReportsController(IHttpClientFactory? httpClientFactory, ILogger<ReportsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> GetReports(CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        _logger.LogInformation("GetReports request initiated by {Username}", username);

        if (!User.Identity?.IsAuthenticated ?? false)
        {
            _logger.LogWarning("GetReports: User not authenticated");
            return Unauthorized();
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        _logger.LogInformation("User {Username} roles: {Roles}", username, string.Join(',', roles));

        var targetUserId =  User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? username;
        _logger.LogInformation("GetReports: Authenticated user: {Username}, requesting reports for targetUserId: {TargetUserId}", username, targetUserId);



        _logger.LogInformation("GetReports: Access granted for user: {Username}, fetching reports from backend", username);

        // Создаём CancellationTokenSource с таймаутом и объединяем с токеном от ASP.NET Core
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));

        try
        {
            var accessToken = await HttpContext.GetTokenAsync("access_token");
            var client = _httpClientFactory?.CreateClient("backend") ?? new HttpClient();
            
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            
            var response = await client.GetAsync($"http://report-api:9999/Reports/{targetUserId}", cts.Token);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GetReports: Failed to fetch reports for user: {Username}, targetUserId: {TargetUserId}, status code: {StatusCode}", username, targetUserId, response.StatusCode);

                return StatusCode((int)response.StatusCode, "Failed to fetch reports from reports-api");
            }
            
            var content = await response.Content.ReadAsStringAsync(cts.Token);
            return Content(content, "application/json");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "GetReports: Request timeout or cancelled for user: {Username}, targetUserId: {TargetUserId}", username, targetUserId);
            return StatusCode(StatusCodes.Status408RequestTimeout, "Request timeout or was cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetReports: HTTP error for user: {Username}, targetUserId: {TargetUserId}", username, targetUserId);
            return StatusCode(StatusCodes.Status502BadGateway, $"Error communicating with reports backend {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetReports: Unexpected error for user: {Username}, targetUserId: {TargetUserId}", username, targetUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred");
        }
    }
}