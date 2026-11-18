using System.Globalization;
using BoxBox.Core.Analytics;
using BoxBox.Core.Models;
using BoxBox.Core.Validation;
using Microsoft.EntityFrameworkCore;

// Single car for now; every record still carries a vehicle id so more cars need no migration.
var DefaultVehicle = Guid.Parse("11111111-1111-1111-1111-111111111111");

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("BoxBox")
    ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "boxbox.db")}";
builder.Services.AddDbContext<BoxBoxDb>(o => o.UseSqlite(connection));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<BoxBoxDb>().Database.EnsureCreated();

app.UseDefaultFiles();
app.UseStaticFiles();

static DateOnly TodayMy()
{
    var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
    return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz));
}

static FuelEntry ToCore(EntryRow r) => new(r.Id, r.VehicleId, r.Date, r.AmountPaid, r.Litres);

app.MapGet("/api/entries", async (BoxBoxDb db) =>
{
    var rows = await db.Entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedUtc).ToListAsync();
    return rows.Select(r => new EntryDto(r.Id, r.Date, r.AmountPaid, r.Litres, ToCore(r).PricePerLitre));
});

app.MapPost("/api/entries", async (NewEntry body, BoxBoxDb db) =>
{
    var entry = new FuelEntry(Guid.NewGuid(), DefaultVehicle, body.Date ?? TodayMy(), body.AmountPaid, body.Litres);
    var errors = FuelEntryValidator.Validate(entry, TodayMy());
    if (errors.Count > 0) return Results.BadRequest(new { errors });

    db.Entries.Add(new EntryRow { Id = entry.Id, VehicleId = entry.VehicleId, Date = entry.Date,
        AmountPaid = entry.AmountPaid, Litres = entry.Litres, CreatedUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();
    return Results.Created($"/api/entries/{entry.Id}", new EntryDto(entry.Id, entry.Date, entry.AmountPaid, entry.Litres, entry.PricePerLitre));
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
    SpendSummary.Compute((await db.Entries.ToListAsync()).Select(ToCore), TodayMy()));

// CSV import: one row per line "date,amount[,litres]" (date as yyyy-MM-dd or dd/MM/yyyy).
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
        { errors.Add($"Line {lineNo}: expected date,amount[,litres]"); continue; }

        decimal? litres = null;
        if (parts.Length > 2 && parts[2].Length > 0)
        {
            if (!decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var l))
            { errors.Add($"Line {lineNo}: litres is not a number"); continue; }
            litres = l;
        }

        var entry = new FuelEntry(Guid.NewGuid(), DefaultVehicle, date, amount, litres);
        var problems = FuelEntryValidator.Validate(entry, today);
        if (problems.Count > 0) { errors.AddRange(problems.Select(p => $"Line {lineNo}: {p}")); continue; }
        parsed.Add(new EntryRow { Id = entry.Id, VehicleId = entry.VehicleId, Date = date, AmountPaid = amount, Litres = litres, CreatedUtc = DateTime.UtcNow });
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

public sealed record NewEntry(DateOnly? Date, decimal AmountPaid, decimal? Litres);
public sealed record EntryDto(Guid Id, DateOnly Date, decimal AmountPaid, decimal? Litres, decimal? PricePerLitre);

public partial class Program;
