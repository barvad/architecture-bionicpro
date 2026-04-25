using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BionicPRO.Reports.Services;

namespace BionicPRO.Reports.Controllers;

[ApiController]
[Route("[controller]")]
public class ReportsController : ControllerBase
{
	private readonly IReportService _reportService;
	private readonly ILogger<ReportsController> _logger;

	public ReportsController(IReportService reportService, ILogger<ReportsController> logger)
	{
		_reportService = reportService;
		_logger = logger;
		_logger.LogInformation("ReportsController initialized");
	}

	[HttpGet("debug")]
	public IActionResult Debug()
	{
		var auth = Request.Headers["Authorization"].ToString();
		return Ok(new { auth });
	}

    /// <summary>
	/// Получает отчёт пользователя за текущий день и возвращает ссылку на CDN
	/// </summary>
	/// <param name="userId">ID пользователя</param>
	/// <returns>Отчёт со ссылкой на CDN</returns>
	[HttpGet("{userId}")]
	[Authorize(Roles = "user")]
	public async Task<IActionResult> GetUserReportForToday(Guid userId)
	{
		_logger.LogInformation("GetUserReportForToday called for userId: {UserId}", userId);

		try
		{
			var today = DateTime.UtcNow.Date.AddDays(-1);
			var report = await _reportService.GetReportForDateAsync(userId, today);

			if (report == null)
			{
				_logger.LogInformation("No report found for userId: {UserId} date: {Date}", userId, today);
				return NotFound();
			}

			_logger.LogInformation("Returning report for userId: {UserId} date: {Date}", userId, report.ReportDate);
			return Ok(report);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving report for userId: {UserId}", userId);
			throw;
		}
	}
}