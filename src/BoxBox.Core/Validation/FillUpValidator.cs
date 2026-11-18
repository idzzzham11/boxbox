using BoxBox.Core.Models;

namespace BoxBox.Core.Validation;

public static class FillUpValidator
{
    /// <summary>
    /// Validates a candidate fill-up against the vehicle's existing history.
    /// Returns an empty list when valid. The candidate's own Id is ignored in the history
    /// so the same method works for edits.
    /// </summary>
    public static IReadOnlyList<string> Validate(FillUp candidate, IEnumerable<FillUp> history, DateOnly today)
    {
        var errors = new List<string>();

        if (candidate.Litres <= 0) errors.Add("Litres must be greater than 0.");
        if (candidate.PricePerLitre <= 0) errors.Add("Price per litre must be greater than 0.");
        if (candidate.OdometerKm < 0) errors.Add("Odometer cannot be negative.");
        if (candidate.Date > today) errors.Add("Date cannot be in the future.");

        var others = history
            .Where(f => f.VehicleId == candidate.VehicleId && f.Id != candidate.Id)
            .ToList();

        // The odometer must be consistent with dates: no earlier-or-same-day record may have a
        // higher reading, and no later record may have a lower one.
        var previous = others.Where(f => f.Date <= candidate.Date)
                             .OrderByDescending(f => f.OdometerKm).FirstOrDefault();
        if (previous is not null && candidate.OdometerKm <= previous.OdometerKm)
            errors.Add($"Odometer must be greater than {previous.OdometerKm} km (fill-up on {previous.Date:yyyy-MM-dd}).");

        var next = others.Where(f => f.Date > candidate.Date)
                         .OrderBy(f => f.OdometerKm).FirstOrDefault();
        if (next is not null && candidate.OdometerKm >= next.OdometerKm)
            errors.Add($"Odometer must be less than {next.OdometerKm} km (fill-up on {next.Date:yyyy-MM-dd}).");

        return errors;
    }
}
