using QualityTrack.Domain.Metrics;
using QualityTrack.Domain.Progress;

namespace QualityTrack.Tests;

public class ProgressCalculatorTests
{
    [Theory]
    [InlineData(60, 80, 60, 0)]
    [InlineData(60, 80, 70, 0.5)]
    [InlineData(60, 80, 80, 1)]
    [InlineData(60, 80, 95, 1)]
    [InlineData(60, 80, 50, 0)]
    public void HigherIsBetter_MeasuresTheWayFromBaselineToTarget(double baseline, double target, double current, double expected) =>
        Assert.Equal(expected, ProgressCalculator.GoalProgress(baseline, target, current, MetricDirection.HigherIsBetter), 6);

    [Theory]
    [InlineData(4, 0, 4, 0)]
    [InlineData(4, 0, 1, 0.75)]
    [InlineData(4, 0, 0, 1)]
    [InlineData(4, 0, 9, 0)]
    public void LowerIsBetter_MeasuresTheWayFromBaselineToTarget(double baseline, double target, double current, double expected) =>
        Assert.Equal(expected, ProgressCalculator.GoalProgress(baseline, target, current, MetricDirection.LowerIsBetter), 6);

    [Fact]
    public void MetricStillAtItsBaseline_IsPlainZeroNotNegativeZero() =>
        Assert.False(double.IsNegative(ProgressCalculator.GoalProgress(60, 80, 60, MetricDirection.HigherIsBetter)));

    [Fact]
    public void GoalAlreadyMetAtBaseline_IsComplete() =>
        Assert.Equal(1, ProgressCalculator.GoalProgress(0, 0, 0, MetricDirection.LowerIsBetter));

    [Fact]
    public void GoalMetAtBaselineThatLaterRegresses_HasNoProgress() =>
        Assert.Equal(0, ProgressCalculator.GoalProgress(0, 0, 2, MetricDirection.LowerIsBetter));

    [Fact]
    public void Overall_IsTheMeanAndNullWithoutGoals()
    {
        Assert.Equal(0.5, ProgressCalculator.Overall([0, 1]));
        Assert.Null(ProgressCalculator.Overall([]));
    }

    [Theory]
    [InlineData(60, 61, MetricDirection.HigherIsBetter, Trend.Improved)]
    [InlineData(60, 59, MetricDirection.HigherIsBetter, Trend.Worsened)]
    [InlineData(3, 0, MetricDirection.LowerIsBetter, Trend.Improved)]
    [InlineData(3, 5, MetricDirection.LowerIsBetter, Trend.Worsened)]
    [InlineData(3, 3, MetricDirection.LowerIsBetter, Trend.Unchanged)]
    [InlineData(100, 200, MetricDirection.Neutral, Trend.Unchanged)]
    public void Judge_SaysWhetherAChangeIsGood(double previous, double current, MetricDirection direction, Trend expected) =>
        Assert.Equal(expected, ProgressCalculator.Judge(previous, current, direction));

    [Fact]
    public void UnknownMetric_IsANeutralCountNamedByItsKey()
    {
        var metric = MetricCatalog.Get("bundle_size_kb");
        Assert.Equal(("bundle_size_kb", MetricUnit.Count, MetricDirection.Neutral), (metric.Name, metric.Unit, metric.Direction));
        Assert.Equal(MetricCategory.Other, metric.Category);
    }

    [Fact]
    public void EveryCatalogMetric_BelongsToQualitySecurityOrMaintainability()
    {
        Assert.DoesNotContain(MetricCatalog.All, m => m.Category == MetricCategory.Other);
        Assert.Equal(MetricCatalog.All.Count, MetricCatalog.All.Select(m => m.Key).Distinct().Count());
        // Kept together by category, in report order, so tables read top to bottom without regrouping.
        Assert.Equal(
            [MetricCategory.Quality, MetricCategory.Security, MetricCategory.Maintainability],
            MetricCatalog.All.Select(m => m.Category).Distinct());
        Assert.Equal(MetricCategory.Security, MetricCatalog.Get("vulnerabilities").Category);
        Assert.Equal(MetricCategory.Maintainability, MetricCatalog.Get("technical_debt_minutes").Category);
        Assert.Equal(MetricCategory.Quality, MetricCatalog.Get("coverage").Category);
    }
}
