using BoxBox.Core.Models;

namespace BoxBox.Tests;

internal static class TestData
{
    public static readonly Guid Car = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static FillUp Fill(string date, int odo, decimal litres, decimal price = 2.05m, bool full = true, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Car, DateOnly.Parse(date), odo, litres, price, full);

    /// <summary>Steady history: every 400 km, ~28 L (7.0 L/100km), every 8 days.</summary>
    public static List<FillUp> Steady(int fillUps, decimal litresPer400 = 28m)
    {
        var list = new List<FillUp>();
        var start = new DateOnly(2026, 1, 1);
        for (var i = 0; i < fillUps; i++)
            list.Add(new FillUp(Guid.NewGuid(), Car, start.AddDays(i * 8), 10_000 + i * 400,
                i == 0 ? 30m : litresPer400, 2.05m));
        return list;
    }
}
