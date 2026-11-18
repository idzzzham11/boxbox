using System.Globalization;

namespace BoxBox.Web.Components;

/// <summary>Display formatting shared by the components (invariant culture so output is the same everywhere).</summary>
public static class Fmt
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string Rm(decimal n) => "RM " + n.ToString("N2", C);
    public static string Rm(decimal? n) => n is null ? "—" : Rm(n.Value);
    public static string RmShort(double n) => "RM " + Math.Round(n).ToString("0", C);
    public static string Litres(decimal n) => n.ToString("0.0", C) + " L";
    public static string Litres(double n) => n.ToString("0.0", C) + " L";
    public static string Num(decimal n, string format = "0.00") => n.ToString(format, C);

    /// <summary>The app is Malaysian: "today" is Malaysia time (UTC+8, no daylight saving), as on the server.</summary>
    public static DateOnly TodayMy() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
}
