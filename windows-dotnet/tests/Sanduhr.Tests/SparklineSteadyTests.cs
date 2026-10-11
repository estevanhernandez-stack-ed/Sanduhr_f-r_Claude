using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SparklineSteadyTests
{
    [Fact]
    public void Steady_horizon_data_draws_a_level()
    {
        double[] v = [96, 96, 96.4, 96, 97.2];
        Assert.Equal(SparklineDraw.Level, SparklineSteady.Drawn(SparklineStyle.Horizon, v));
    }

    [Fact]
    public void Moving_horizon_data_stays_horizon()
    {
        double[] v = [40, 52, 61, 70];
        Assert.Equal(SparklineDraw.Horizon, SparklineSteady.Drawn(SparklineStyle.Horizon, v));
    }

    [Fact]
    public void Spread_of_two_and_a_half_stays_horizon()
    {
        double[] v = [95, 97.5];
        Assert.Equal(SparklineDraw.Horizon, SparklineSteady.Drawn(SparklineStyle.Horizon, v));
    }

    [Fact]
    public void Spread_of_exactly_two_stays_horizon()
    {
        double[] v = [95, 97];
        Assert.Equal(SparklineDraw.Horizon, SparklineSteady.Drawn(SparklineStyle.Horizon, v));
    }

    [Fact]
    public void Classic_stays_classic_even_when_flat()
    {
        double[] v = [96, 96, 96];
        Assert.Equal(SparklineDraw.Classic, SparklineSteady.Drawn(SparklineStyle.Classic, v));
    }

    [Fact]
    public void Horizon_with_no_values_stays_horizon()
    {
        Assert.Equal(SparklineDraw.Horizon, SparklineSteady.Drawn(SparklineStyle.Horizon, []));
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(50, 8)]
    [InlineData(100, 1)]
    [InlineData(150, 1)]
    [InlineData(-20, 15)]
    public void LevelY_maps_the_mean_onto_the_height(double mean, double expectedY)
    {
        Assert.Equal(expectedY, SparklineSteady.LevelY([mean, mean], 16));
    }
}
