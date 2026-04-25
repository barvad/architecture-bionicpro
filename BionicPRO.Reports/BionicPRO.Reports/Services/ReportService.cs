using System.Text.Json;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.ADO.Parameters;
using BionicPRO.Reports.Models;

namespace BionicPRO.Reports.Services;

/// <summary>
/// Сервис для работы с отчётами
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Получает или генерирует отчёт пользователя за указанную дату
    /// </summary>
    Task<ReportResponse?> GetReportForDateAsync(Guid userId, DateTime date);
}

/// <summary>
/// Реализация сервиса для работы с отчётами
/// </summary>
public class ReportService : IReportService
{
    private readonly string _connectionString;
    private readonly IMinioService _minioService;
    private readonly ILogger<ReportService> _logger;
    private readonly string _cdnBaseUrl;

    public ReportService(
        IConfiguration configuration,
        IMinioService minioService,
        ILogger<ReportService> logger)
    {
        _connectionString = configuration.GetConnectionString("ClickHouse")
            ?? throw new InvalidOperationException("Connection string 'ClickHouse' not found");
        _minioService = minioService;
        _logger = logger;
        _cdnBaseUrl = configuration["Minio:CdnUrl"]
            ?? throw new InvalidOperationException("Minio CdnUrl not configured");
    }

    public async Task<ReportResponse?> GetReportForDateAsync(Guid userId, DateTime date)
    {
        _logger.LogInformation("Getting report for userId: {UserId} date: {Date}", userId, date.Date);

        try
        {
            using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync();
            _logger.LogInformation("ClickHouse connection opened successfully");

            using var command = connection.CreateCommand();
            // Select rows for the specified date (inclusive start, exclusive next day)
            command.CommandText = "SELECT * FROM daily_user_reports WHERE user_id = @userId AND report_date >= @start AND report_date < @end ORDER BY report_date DESC LIMIT 1";
            var start = date.Date;
            var end = start.AddDays(1);
            command.Parameters.Add(new ClickHouseDbParameter()
            {
                ParameterName = "userId",
                Value = userId,
                DbType = System.Data.DbType.Guid
            });
            command.Parameters.Add(new ClickHouseDbParameter()
            {
                ParameterName = "start",
                Value = start,
                DbType = System.Data.DbType.DateTime
            });
            command.Parameters.Add(new ClickHouseDbParameter()
            {
                ParameterName = "end",
                Value = end,
                DbType = System.Data.DbType.DateTime
            });

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                _logger.LogInformation("No report row found in ClickHouse for userId: {UserId} date: {Date}", userId, start);
                return null;
            }

            var reportData = new DailyReportData
            {
                UserId = reader.GetFieldValue<Guid>(0),
                ReportDate = reader.GetFieldValue<DateTime>(1),
                FullName = reader.GetString(2),
                ProstheticModel = reader.GetString(3),
                StepsCount = reader.GetFieldValue<uint>(4),
                AvgBatteryLevel = reader.GetFieldValue<float>(5),
                ErrorsCount = reader.GetFieldValue<byte>(6)
            };

            var reportResponse = await ProcessReportAsync(reportData);
            _logger.LogInformation("Returning report for {UserId} date: {Date}", userId, reportData.ReportDate);
            return reportResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving report for userId: {UserId} date: {Date}", userId, date);
            throw;
        }
    }

    private async Task<ReportResponse> ProcessReportAsync(DailyReportData reportData)
    {
        var filePath = _minioService.GetFilePath(reportData.UserId, reportData.ReportDate);

        // Проверяем, существует ли отчёт в MinIO
        bool fileExists = await _minioService.FileExistsAsync(reportData.UserId, reportData.ReportDate);

        if (!fileExists)
        {
            // Формируем отчёт в JSON формате
            var jsonContent = JsonSerializer.Serialize(reportData, new JsonSerializerOptions { WriteIndented = true });

            // Загружаем в MinIO
            await _minioService.UploadReportAsync(reportData.UserId, reportData.ReportDate, jsonContent);
            _logger.LogInformation("Generated and uploaded report for {UserId} on {Date}",
                reportData.UserId, reportData.ReportDate);
        }

        // Формируем URL на CDN
        var reportUrl = $"{_cdnBaseUrl}/reports/{filePath}";

        return new ReportResponse
        {
            ReportDate = reportData.ReportDate,
            ReportUrl = reportUrl,
            Status = fileExists ? "cached" : "generated"
        };
    }
}
