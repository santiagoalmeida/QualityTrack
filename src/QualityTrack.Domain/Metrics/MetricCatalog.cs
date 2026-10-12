namespace QualityTrack.Domain.Metrics;

/// <summary>
/// Metrics the product knows how to name, format and judge. Reporters may send any other key:
/// it is stored and shown, but treated as a neutral count.
/// </summary>
public static class MetricCatalog
{
    public const string QualityGate = "quality_gate";
    public const string Coverage = "coverage";
    public const string Bugs = "bugs";
    public const string Vulnerabilities = "vulnerabilities";
    public const string CodeSmells = "code_smells";
    public const string DuplicatedLinesDensity = "duplicated_lines_density";
    public const string TechnicalDebtMinutes = "technical_debt_minutes";
    public const string TestsTotal = "tests_total";
    public const string TestsPassed = "tests_passed";
    public const string TestsFailed = "tests_failed";
    public const string TestsSkipped = "tests_skipped";
    public const string TestsDurationSeconds = "tests_duration_seconds";

    /// <summary>Report order: quality, then security, then maintainability; unknown metrics last.</summary>
    public static IReadOnlyList<MetricCategory> Categories { get; } =
        [MetricCategory.Quality, MetricCategory.Security, MetricCategory.Maintainability, MetricCategory.Other];

    // Listed in display order. Every measure SonarQube reports belongs to one of the three categories.
    private static readonly MetricDefinition[] Known =
    [
        new(QualityGate, "Quality gate", MetricUnit.Flag, MetricDirection.HigherIsBetter, MetricCategory.Quality),
        new("reliability_rating", "Nota de fiabilidad", MetricUnit.Rating, MetricDirection.LowerIsBetter, MetricCategory.Quality),
        new(Bugs, "Bugs", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Quality),
        new(Coverage, "Cobertura", MetricUnit.Percent, MetricDirection.HigherIsBetter, MetricCategory.Quality),
        new("line_coverage", "Cobertura de líneas", MetricUnit.Percent, MetricDirection.HigherIsBetter, MetricCategory.Quality),
        new("branch_coverage", "Cobertura de ramas", MetricUnit.Percent, MetricDirection.HigherIsBetter, MetricCategory.Quality),
        new(TestsTotal, "Tests ejecutados", MetricUnit.Count, MetricDirection.HigherIsBetter, MetricCategory.Quality),
        new(TestsPassed, "Tests aprobados", MetricUnit.Count, MetricDirection.HigherIsBetter, MetricCategory.Quality),
        new(TestsFailed, "Tests fallidos", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Quality),
        new(TestsSkipped, "Tests omitidos", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Quality),
        new(TestsDurationSeconds, "Duración de tests", MetricUnit.Seconds, MetricDirection.Neutral, MetricCategory.Quality),

        new("security_rating", "Nota de seguridad", MetricUnit.Rating, MetricDirection.LowerIsBetter, MetricCategory.Security),
        new(Vulnerabilities, "Vulnerabilidades", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Security),
        new("security_review_rating", "Nota de revisión de seguridad", MetricUnit.Rating, MetricDirection.LowerIsBetter, MetricCategory.Security),
        new("security_hotspots", "Security hotspots", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Security),
        new("security_hotspots_reviewed", "Hotspots revisados", MetricUnit.Percent, MetricDirection.HigherIsBetter, MetricCategory.Security),

        new("maintainability_rating", "Nota de mantenibilidad", MetricUnit.Rating, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new(CodeSmells, "Code smells", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new(TechnicalDebtMinutes, "Deuda técnica", MetricUnit.Minutes, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new("technical_debt_ratio", "Ratio de deuda técnica", MetricUnit.Percent, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new(DuplicatedLinesDensity, "Duplicación", MetricUnit.Percent, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new("duplicated_lines", "Líneas duplicadas", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new("duplicated_blocks", "Bloques duplicados", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new("open_issues", "Incidencias abiertas", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new("blocker_issues", "Incidencias bloqueantes", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        new("critical_issues", "Incidencias críticas", MetricUnit.Count, MetricDirection.LowerIsBetter, MetricCategory.Maintainability),
        // Size and complexity grow with the product: a change is information, not a verdict.
        new("complexity", "Complejidad ciclomática", MetricUnit.Count, MetricDirection.Neutral, MetricCategory.Maintainability),
        new("cognitive_complexity", "Complejidad cognitiva", MetricUnit.Count, MetricDirection.Neutral, MetricCategory.Maintainability),
        new("lines_of_code", "Líneas de código", MetricUnit.Count, MetricDirection.Neutral, MetricCategory.Maintainability)
    ];

    private static readonly Dictionary<string, (MetricDefinition Definition, int Order)> ByKey =
        Known.Select((d, i) => (d, i)).ToDictionary(x => x.d.Key, x => (x.d, x.i), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<MetricDefinition> All => Known;

    public static MetricDefinition Get(string key) =>
        ByKey.TryGetValue(key, out var known)
            ? known.Definition
            : new MetricDefinition(key, key, MetricUnit.Count, MetricDirection.Neutral, MetricCategory.Other);

    /// <summary>Display order: catalog order first, unknown metrics after, alphabetically.</summary>
    public static int Order(string key) => ByKey.TryGetValue(key, out var known) ? known.Order : int.MaxValue;

    /// <summary>Targets assigned to a project the first time it reports one of these metrics.</summary>
    public static IReadOnlyDictionary<string, double> DefaultTargets { get; } = new Dictionary<string, double>
    {
        [Coverage] = 80,
        [Bugs] = 0,
        [Vulnerabilities] = 0,
        [TestsFailed] = 0,
        [DuplicatedLinesDensity] = 3
    };
}
