using BoxBox.Core.Validation;
using static BoxBox.Tests.TestData;

namespace BoxBox.Tests;

public class FillUpValidatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void Valid_fill_up_has_no_errors()
    {
        var history = new[] { Fill("2026-10-01", 10_000, 30) };
        Assert.Empty(FillUpValidator.Validate(Fill("2026-10-10", 10_400, 28), history, Today));
    }

    [Theory]
    [InlineData(0, 2.05)]
    [InlineData(-1, 2.05)]
    [InlineData(28, 0)]
    [InlineData(28, -2)]
    public void Non_positive_litres_or_price_rejected(double litres, double price)
    {
        var errors = FillUpValidator.Validate(Fill("2026-10-10", 10_400, (decimal)litres, (decimal)price), [], Today);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Odometer_must_increase()
    {
        var history = new[] { Fill("2026-10-01", 10_000, 30) };
        Assert.Single(FillUpValidator.Validate(Fill("2026-10-10", 10_000, 28), history, Today));
        Assert.Single(FillUpValidator.Validate(Fill("2026-10-10", 9_000, 28), history, Today));
    }

    [Fact]
    public void Back_dated_entry_must_fit_between_neighbours()
    {
        var history = new[] { Fill("2026-10-01", 10_000, 30), Fill("2026-10-20", 10_800, 30) };
        Assert.Empty(FillUpValidator.Validate(Fill("2026-10-10", 10_400, 28), history, Today));
        Assert.NotEmpty(FillUpValidator.Validate(Fill("2026-10-10", 10_900, 28), history, Today));
    }

    [Fact]
    public void Editing_does_not_conflict_with_itself()
    {
        var existing = Fill("2026-10-10", 10_400, 28);
        var edited = existing with { Litres = 27 };
        Assert.Empty(FillUpValidator.Validate(edited, [existing], Today));
    }

    [Fact]
    public void Future_date_rejected()
    {
        Assert.NotEmpty(FillUpValidator.Validate(Fill("2026-12-01", 10_400, 28), [], Today));
    }

    [Fact]
    public void Other_vehicles_are_ignored()
    {
        var other = Fill("2026-10-01", 50_000, 30) with { VehicleId = Guid.NewGuid() };
        Assert.Empty(FillUpValidator.Validate(Fill("2026-10-10", 10_400, 28), [other], Today));
    }
}
