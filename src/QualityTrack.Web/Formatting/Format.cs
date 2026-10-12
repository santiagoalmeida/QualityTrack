using System.Globalization;
using QualityTrack.Application.Dashboard;
using QualityTrack.Domain.Metrics;

namespace QualityTrack.Web.Formatting;

/// <summary>Display formatting for metric values, deltas and dates (Spanish UI).</summary>
public static class Format
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-ES");
    private static readonly string[] RatingLetters = ["A", "B", "C", "D", "E"];

    public static string Value(MetricDefinition metric, double value) => metric.Unit switch
    {
        MetricUnit.Percent => $"{Number(value)} %",
        MetricUnit.Minutes => Math.Abs(value) >= 60 ? $"{Number(Math.Round(value / 60))} h" : $"{Number(value)} min",
        MetricUnit.Seconds => $"{Number(value)} s",
        MetricUnit.Rating => RatingLetters[Math.Clamp((int)Math.Round(value), 1, 5) - 1],
        MetricUnit.Flag => value >= 1 ? "OK" : "Falló",
        _ => Number(value)
    };

    /// <summary>Change between two runs: signed for quantities, "antes X" for ratings and flags.</summary>
    public static string Delta(MetricComparison comparison)
    {
        if (comparison.Previous is not { } previous)
            return "—";
        if (comparison.Current == previous)
            return "sin cambio";

        var metric = comparison.Metric;
        var delta = comparison.Current - previous;
        var sign = delta > 0 ? "+" : "−";
        var magnitude = Math.Abs(delta);
        return metric.Unit switch
        {
            MetricUnit.Rating or MetricUnit.Flag => $"antes {Value(metric, previous)}",
            MetricUnit.Percent => $"{sign}{Number(magnitude)} pt",
            MetricUnit.Minutes => magnitude >= 60 ? $"{sign}{Number(Math.Round(magnitude / 60))} h" : $"{sign}{Number(magnitude)} min",
            MetricUnit.Seconds => $"{sign}{Number(magnitude)} s",
            _ => $"{sign}{Number(magnitude)}"
        };
    }

    public static string Category(MetricCategory category) => category switch
    {
        MetricCategory.Quality => "Calidad",
        MetricCategory.Security => "Seguridad",
        MetricCategory.Maintainability => "Mantenibilidad y deuda técnica",
        _ => "Otras métricas"
    };

    public static string Severity(string? severity) => severity switch
    {
        "blocker" => "Bloqueante",
        "critical" => "Crítica",
        "high" => "Alta",
        "major" => "Mayor",
        "medium" => "Media",
        "minor" => "Menor",
        "low" => "Baja",
        "info" => "Informativa",
        null => "—",
        _ => severity
    };

    /// <summary>CSS modifier for a severity chip: the two worst levels stand out.</summary>
    public static string SeverityClass(string? severity) => severity switch
    {
        "blocker" or "critical" or "high" => "chip-bad",
        "major" or "medium" => "chip-warn",
        _ => ""
    };

    public static string Number(double value) =>
        value.ToString(value == Math.Floor(value) ? "N0" : "0.##", Culture);

    /// <summary>A 0..1 fraction as a whole percentage.</summary>
    public static string Percent(double fraction) => $"{Math.Round(fraction * 100).ToString("N0", Culture)} %";

    /// <summary>Difference between two 0..1 fractions, in signed percentage points.</summary>
    public static string Points(double fractionDelta)
    {
        var points = Math.Round(fractionDelta * 100, 1);
        return points == 0 ? "0 pt" : $"{(points > 0 ? "+" : "−")}{Math.Abs(points).ToString("0.#", Culture)} pt";
    }

    /// <summary>Invariant form for HTML number inputs and SVG coordinates.</summary>
    public static string Invariant(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Date(DateTime utc) => utc.ToString("d MMM yyyy, HH:mm", Culture) + " UTC";

    public static string ShortDate(DateTime utc) => utc.ToString("d MMM", Culture);

    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var span = nowUtc - utc;
        return span switch
        {
            { TotalMinutes: < 1 } => "hace un momento",
            { TotalMinutes: < 60 } => $"hace {(int)span.TotalMinutes} min",
            { TotalHours: < 24 } => $"hace {(int)span.TotalHours} h",
            { TotalDays: < 30 } => $"hace {(int)span.TotalDays} d",
            _ => ShortDate(utc)
        };
    }

    public static string ShortCommit(string? sha) => sha is { Length: > 0 } ? sha[..Math.Min(7, sha.Length)] : "sin commit";
}
