namespace BoxBox.Core.Contracts;

// Contracts are the shapes of the JSON that travels between the API and any client.
// They live in Core so the ASP.NET API and the Blazor web app compile against the SAME types:
// rename a field here and both projects fail to build until they agree again.

/// <summary>Body of POST /api/entries. Only AmountPaid is required.</summary>
public sealed record NewEntry(DateOnly? Date, decimal AmountPaid, decimal? Litres, int? TripKm = null);

/// <summary>One history row. Litres are recorded, or estimated from the amount (LitresEstimated = true).</summary>
public sealed record EntryDto(
    Guid Id, DateOnly Date, decimal AmountPaid, decimal? Litres,
    decimal? PricePerLitre, int? TripKm, bool LitresEstimated);
