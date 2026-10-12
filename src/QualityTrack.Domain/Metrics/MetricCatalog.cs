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

    public const string GroupTests = "Tests";
    public const string GroupReliability = "Fiabilidad";
    public const string GroupSecurity = "Seguridad";
    public const string GroupMaintainability = "Mantenibilidad";
    public const string GroupCoverage = "Cobertura";
    public const string GroupSize = "Tamaño";
    public const string GroupOther = "Otras";

    private static readonly MetricDefinition[] Known =
    [
        new(QualityGate, "Quality gate", MetricUnit.Flag, MetricDirection.HigherIsBetter, GroupReliability),
        new(TestsTotal, "Tests ejecutados", MetricUnit.Count, MetricDirection.HigherIsBetter, GroupTests),
        new(TestsPassed, "Tests aprobados", MetricUnit.Count, MetricDirection.HigherIsBetter, GroupTests),
        new(TestsFailed, "Tests fallidos", MetricUnit.Count, MetricDirection.LowerIsBetter, GroupTests),
        new(TestsSkipped, "Tests omitidos", MetricUnit.Count, MetricDirection.LowerIsBetter, GroupTests),
        new(TestsDurationSeconds, "Duración de tests", MetricUnit.Seconds, MetricDirection.Neutral, GroupTests),
        new(Coverage, "Cobertura", MetricUnit.Percent, MetricDirection.HigherIsBetter, GroupCoverage),
        new("line_coverage", "Cobertura de líneas", MetricUnit.Percent, MetricDirection.HigherIsBetter, GroupCoverage),
        new("branch_coverage", "Cobertura de ramas", MetricUnit.Percent, MetricDirection.HigherIsBetter, GroupCoverage),
        new("reliability_rating", "Nota de fiabilidad", MetricUnit.Rating, MetricDirection.LowerIsBetter, GroupReliability),
        new(Bugs, "Bugs", MetricUnit.Count, MetricDirection.LowerIsBetter, GroupReliability),
        new("security_rating", "Nota de seguridad", MetricUnit.Rating, MetricDirection.LowerIsBetter, GroupSecurity),
        new(Vulnerabilities, "Vulnerabilidades", MetricUnit.Count, MetricDirection.LowerIsBetter, GroupSecurity),
        new("security_hotspots", "Security hotspots", MetricUnit.Count, MetricDirection.LowerIsBetter, GroupSecurity),
        new("maintainability_rating", "Nota de mantenibilidad", MetricUnit.Rating, MetricDirection.LowerIsBetter, GroupMaintainability),
        new(CodeSmells, "Code smells", MetricUnit.Count, MetricDirection.LowerIsBetter, GroupMaintainability),
        new(TechnicalDebtMinutes, "Deuda técnica", MetricUnit.Minutes, MetricDirection.LowerIsBetter, GroupMaintainability),
        new(DuplicatedLinesDensity, "Duplicación", MetricUnit.Percent, MetricDirection.LowerIsBetter, GroupMaintainability),
        new("open_issues", "Incidencias abiertas", MetricUnit.Count, MetricDirection.LowerIsBetter, GroupMaintainability),
        new("lines_of_code", "Líneas de código", MetricUnit.Count, MetricDirection.Neutral, GroupSize)
    ];

    private static readonly Dictionary<string, (MetricDefinition Definition, int Order)> ByKey =
        Known.Select((d, i) => (d, i)).ToDictionary(x => x.d.Key, x => (x.d, x.i), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<MetricDefinition> All => Known;

    public static MetricDefinition Get(string key) =>
        ByKey.TryGetValue(key, out var known)
            ? known.Definition
            : new MetricDefinition(key, key, MetricUnit.Count, MetricDirection.Neutral, GroupOther);

    /// <summary>Display order: catalog order first, unknown metrics after, alphabetically.</summary>
    public static int Order(string key) => ByKey.TryGetValue(key, out var known) ? known.Order : int.MaxValue;

    /// <summary>Targets assigned to a component the first time it reports one of these metrics.</summary>
    public static IReadOnlyDictionary<string, double> DefaultTargets { get; } = new Dictionary<string, double>
    {
        [Coverage] = 80,
        [Bugs] = 0,
        [Vulnerabilities] = 0,
        [TestsFailed] = 0,
        [DuplicatedLinesDensity] = 3
    };
}
