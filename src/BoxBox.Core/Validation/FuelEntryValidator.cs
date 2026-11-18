using BoxBox.Core.Models;

namespace BoxBox.Core.Validation;

public static class FuelEntryValidator
{
    public static IReadOnlyList<string> Validate(FuelEntry entry, DateOnly today)
    {
        var errors = new List<string>();
        if (entry.AmountPaid <= 0) errors.Add("Amount paid must be greater than 0.");
        if (entry.Litres is <= 0) errors.Add("Litres must be greater than 0 when provided.");
        if (entry.Date > today) errors.Add("Date cannot be in the future.");
        return errors;
    }
}
