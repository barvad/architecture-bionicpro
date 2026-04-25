namespace BionicPRO.Reports.Models;

/// <summary>
/// Модель отчёта для сохранения в MinIO
/// </summary>
public class DailyReportData
{
    public Guid UserId { get; set; }
    public DateTime ReportDate { get; set; }
    public string FullName { get; set; }
    public string ProstheticModel { get; set; }
    public ulong StepsCount { get; set; }
    public float AvgBatteryLevel { get; set; }
    public uint ErrorsCount { get; set; }
}
