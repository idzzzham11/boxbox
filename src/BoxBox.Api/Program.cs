using System.Globalization;
using BoxBox.Api;
using BoxBox.Core.Analytics;
using BoxBox.Core.Contracts;
using BoxBox.Core.Models;
using BoxBox.Core.Validation;
using Microsoft.EntityFrameworkCore;

// Single car for now; every record still carries a vehicle id so more cars need no migration.
var DefaultVehicle = Guid.Parse("11111111-1111-1111-1111-111111111111");

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("BoxBox")
    ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "boxbox.db")}";
builder.Services.AddDbContext<BoxBoxDb>(o => o.UseSqlite(connection));

// Pump price used to estimate litres when only the amount paid is known (subsidised BUDI95 RON95 here).
// Configure under FuelPrices:Budi95 in appsettings.json: one entry per price change, newest From wins.
var prices = new FuelPriceSchedule(
    (builder.Configuration.GetSection("FuelPrices:Budi95").Get<List<PriceConfig>>() ?? [])
        .Where(p => DateOnly.TryParse(p.From, CultureInfo.InvariantCulture, out _))
        .Select(p => new PricePeriod(DateOnly.Parse(p.From, CultureInfo.InvariantCulture), p.PerLitre)));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BoxBoxDb>();
    db.Database.EnsureCreated();
    // EnsureCreated never alters an existing table, so add columns introduced later (idempotent).
    var cols = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Entries')").ToList();
    if (!cols.Contains("TripKm"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Entries ADD COLUMN TripKm INTEGER NULL");
    // Note: databases that ran an intermediate build may still have an unused IsFullTank column
    // (NOT NULL DEFAULT 0). It is ignored and inserts keep working through its default.
}

app.UseBoxBoxStaticFiles(); // classic page at / and the Blazor app at /next/ (see StaticAssets.cs)

static DateOnly TodayMy()
{
    var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
    return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz));
}

EntryDto ToDto(FuelEntry e, FuelPriceSchedule schedule)
{
    var r = schedule.Resolve(e);
    return new EntryDto(e.Id, e.Date, e.AmountPaid, r.Litres, r.PricePerLitre, e.TripKm, r.Estimated);
}

static FuelEntry ToCore(EntryRow r) => new(r.Id, r.VehicleId, r.Date, r.AmountPaid, r.Litres, r.TripKm);

app.MapGet("/api/entries", async (BoxBoxDb db) =>
{
    var rows = await db.Entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedUtc).ToListAsync();
    return rows.Select(r => ToDto(ToCore(r), prices));
});

app.MapPost("/api/entries", async (NewEntry body, BoxBoxDb db) =>
{
    var entry = new FuelEntry(Guid.NewGuid(), DefaultVehicle, body.Date ?? TodayMy(), body.AmountPaid, body.Litres, body.TripKm);
    var errors = FuelEntryValidator.Validate(entry, TodayMy());
    if (errors.Count > 0) return Results.BadRequest(new { errors });

    db.Entries.Add(new EntryRow { Id = entry.Id, VehicleId = entry.VehicleId, Date = entry.Date,
        AmountPaid = entry.AmountPaid, Litres = entry.Litres, TripKm = entry.TripKm,
        CreatedUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();
    return Results.Created($"/api/entries/{entry.Id}", ToDto(entry, prices));
});

app.MapDelete("/api/entries/{id:guid}", async (Guid id, BoxBoxDb db) =>
{
    var row = await db.Entries.FindAsync(id);
    if (row is null) return Results.NotFound();
    db.Entries.Remove(row);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapGet("/api/summary", async (BoxBoxDb db) =>
    SpendSummary.Compute((await db.Entries.OrderBy(e => e.Date).ThenBy(e => e.CreatedUtc).ToListAsync()).Select(ToCore), TodayMy(), prices));

app.MapGet("/api/forecast", async (BoxBoxDb db) =>
    SpendForecaster.Compute((await db.Entries.OrderBy(e => e.Date).ThenBy(e => e.CreatedUtc).ToListAsync()).Select(ToCore), TodayMy(), prices));

// CSV import: one row per line "date,amount[,litres[,tripKm]]" (date as yyyy-MM-dd or dd/MM/yyyy).
// Rows are stored in file order, which matters for same-day fills.
// A header line and blank lines are skipped. All-or-nothing: any bad line rejects the whole file.
app.MapPost("/api/import", async (HttpRequest req, BoxBoxDb db) =>
{
    using var reader = new StreamReader(req.Body);
    var text = await reader.ReadToEndAsync();
    var today = TodayMy();
    var parsed = new List<EntryRow>();
    var errors = new List<string>();
    var lineNo = 0;

    foreach (var raw in text.Split('\n'))
    {
        lineNo++;
        var line = raw.Trim();
        if (line.Length == 0) continue;
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (lineNo == 1 && !char.IsDigit(parts[0].FirstOrDefault())) continue; // header

        if (!DateOnly.TryParseExact(parts[0], ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || parts.Length < 2
            || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        { errors.Add($"Line {lineNo}: expected date,amount[,litres[,tripKm]]"); continue; }

        decimal? litres = null;
        if (parts.Length > 2 && parts[2].Length > 0)
        {
            if (!decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var l))
            { errors.Add($"Line {lineNo}: litres is not a number"); continue; }
            litres = l;
        }

        int? tripKm = null;
        if (parts.Length > 3 && parts[3].Length > 0)
        {
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
            { errors.Add($"Line {lineNo}: trip km is not a whole number"); continue; }
            tripKm = t;
        }

        var entry = new FuelEntry(Guid.NewGuid(), DefaultVehicle, date, amount, litres, tripKm);
        var problems = FuelEntryValidator.Validate(entry, today);
        if (problems.Count > 0) { errors.AddRange(problems.Select(p => $"Line {lineNo}: {p}")); continue; }
        parsed.Add(new EntryRow { Id = entry.Id, VehicleId = entry.VehicleId, Date = date, AmountPaid = amount, Litres = litres, TripKm = tripKm,
            CreatedUtc = DateTime.UtcNow.AddTicks(parsed.Count) });
    }

    if (errors.Count > 0) return Results.BadRequest(new { errors });
    db.Entries.AddRange(parsed);
    await db.SaveChangesAsync();
    return Results.Ok(new { imported = parsed.Count });
});

app.Run();

public sealed class EntryRow
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateOnly Date { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal? Litres { get; set; }
    public int? TripKm { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class BoxBoxDb(DbContextOptions<BoxBoxDb> options) : DbContext(options)
{
    public DbSet<EntryRow> Entries => Set<EntryRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<EntryRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Date);
            // SQLite has no decimal type; store as text to avoid double rounding.
            e.Property(x => x.AmountPaid).HasConversion<string>();
            e.Property(x => x.Litres).HasConversion<string>();
        });
    }
}

public sealed class PriceConfig { public string From { get; set; } = ""; public decimal PerLitre { get; set; } }

public partial class Program;
