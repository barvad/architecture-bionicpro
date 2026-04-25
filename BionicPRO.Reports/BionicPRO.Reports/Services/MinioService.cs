using Minio;
using Minio.DataModel.Args;

namespace BionicPRO.Reports.Services;

/// <summary>
/// Сервис для работы с MinIO хранилищем
/// </summary>
public interface IMinioService
{
    /// <summary>
    /// Проверяет существование файла отчёта в MinIO
    /// </summary>
    Task<bool> FileExistsAsync(Guid userId, DateTime reportDate);

    /// <summary>
    /// Загружает отчёт в MinIO
    /// </summary>
    Task UploadReportAsync(Guid userId, DateTime reportDate, string jsonContent);

    /// <summary>
    /// Получает путь файла в MinIO
    /// </summary>
    string GetFilePath(Guid userId, DateTime reportDate);
}

/// <summary>
/// Реализация сервиса для работы с MinIO
/// </summary>
public class MinioService : IMinioService
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<MinioService> _logger;
    private readonly string _bucketName = "reports";

    public MinioService(IMinioClient minioClient, ILogger<MinioService> logger)
    {
        _minioClient = minioClient;
        _logger = logger;
    }

    public string GetFilePath(Guid userId, DateTime reportDate)
    {
        return $"{userId:D}/{reportDate:yyyy}/{reportDate:MM}/{reportDate:dd}.json";
    }

    public async Task<bool> FileExistsAsync(Guid userId, DateTime reportDate)
    {
        try
        {
            var filePath = GetFilePath(userId, reportDate);
            _logger.LogInformation("Checking if file exists in MinIO: {FilePath}", filePath);

            var statArgs = new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(filePath);

            await _minioClient.StatObjectAsync(statArgs);
            _logger.LogInformation("File exists in MinIO: {FilePath}", filePath);
            return true;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            _logger.LogInformation("File not found in MinIO: {FilePath}", GetFilePath(userId, reportDate));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking file existence in MinIO for userId: {UserId}, date: {Date}",
                userId, reportDate);
            throw;
        }
    }

    public async Task UploadReportAsync(Guid userId, DateTime reportDate, string jsonContent)
    {
        try
        {
            var filePath = GetFilePath(userId, reportDate);
            _logger.LogInformation("Uploading report to MinIO: {FilePath}", filePath);

            var bytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);
            var tempFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid() + ".json");

            try
            {
                await System.IO.File.WriteAllBytesAsync(tempFilePath, bytes);

                var putArgs = new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(filePath)
                    .WithFileName(tempFilePath)
                    .WithContentType("application/json");

                await _minioClient.PutObjectAsync(putArgs);

                _logger.LogInformation("Report successfully uploaded to MinIO: {FilePath}", filePath);
            }
            finally
            {
                if (System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading report to MinIO for userId: {UserId}, date: {Date}",
                userId, reportDate);
            throw;
        }
    }
}
