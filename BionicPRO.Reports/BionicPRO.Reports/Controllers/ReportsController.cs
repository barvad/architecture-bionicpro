using ClickHouse.Driver.ADO;
using ClickHouse.Driver.ADO.Parameters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BionicPRO.Reports.Controllers;

[ApiController]
[Route("[controller]")]
public class ReportsController : ControllerBase
{
    private readonly string _connectionString;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(IConfiguration configuration, ILogger<ReportsController> logger)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("ClickHouse")
            ?? throw new InvalidOperationException("Connection string 'ClickHouse' not found");
        _logger.LogInformation("ReportsController initialized with ClickHouse connection");
    }
    [HttpGet("debug")]
    public IActionResult Debug()
    {
        var auth = Request.Headers["Authorization"].ToString();
        return Ok(new { auth });
    }
    [HttpGet("{userId}")]
	[Authorize(Roles = "user")]
	public async Task<IActionResult> GetUserReports(Guid userId)
	{
		_logger.LogInformation("GetUserReports called for userId: {UserId}", userId);

		var reports = new List<UserReport>();

		try
		{
			using var connection = new ClickHouseConnection(_connectionString);
			await connection.OpenAsync();
			_logger.LogInformation("ClickHouse connection opened successfully");

			using var command = connection.CreateCommand();
			command.CommandText = "SELECT * FROM daily_user_reports WHERE user_id = @userId ORDER BY report_date DESC";
			command.Parameters.Add(new ClickHouseDbParameter(){ ParameterName = "userId", Value = userId, DbType = System.Data.DbType.Guid });

			using var reader = await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				reports.Add(new UserReport
				{
					UserId = reader.GetFieldValue<Guid>(0),
					ReportDate = reader.GetFieldValue<DateTime>(1),
					FullName = reader.GetString(2),
					ProstheticModel = reader.GetString(3),
					StepsCount = reader.GetFieldValue<uint>(4),
					AvgBatteryLevel = reader.GetFieldValue<float>(5),
					ErrorsCount = reader.GetFieldValue<byte>(6)
				});
			}

			_logger.LogInformation("Retrieved {ReportCount} reports for userId: {UserId}", reports.Count, userId);

			if (reports.Count == 0)
			{
				_logger.LogInformation("No reports found for userId: {UserId}", userId);
				return NotFound();
			}

			return Ok(reports);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving reports for userId: {UserId}", userId);
			throw;
		}
	}
}