namespace BionicPRO.Reports.Models;

/// <summary>
/// Ответ API с ссылкой на отчёт
/// </summary>
public class ReportResponse
{
    /// <summary>
    /// Дата отчёта
    /// </summary>
    public DateTime ReportDate { get; set; }

    /// <summary>
    /// URL на CDN для скачивания отчёта
    /// </summary>
    public string ReportUrl { get; set; }

    /// <summary>
    /// Статус генерации отчёта
    /// </summary>
    public string Status { get; set; } // "generated", "cached"
}
