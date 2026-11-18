# Learning .NET with BoxBox

A guided tour of this repository. Each concept points at the file where you can see it in real code, then
suggests a small exercise. Do them in order; every exercise is safe because `dotnet test` tells you if you broke
something.

Run everything from the repository root. `dotnet build` compiles all projects, `dotnet test` runs the tests.

## 1. Solution and projects

| Project | Kind | Job |
|---|---|---|
| `src/BoxBox.Core` | class library | Pure logic: models, validation, analytics. No database, no web. |
| `src/BoxBox.Api` | ASP.NET Core minimal API | HTTP endpoints, SQLite database, serves the web pages. |
| `src/BoxBox.Web` | Blazor WebAssembly | The new front end. C# that runs in the browser. |
| `tests/BoxBox.Tests` | xUnit | Tests for all of the above. |

`BoxBox.slnx` ties them together. A project reference (`<ProjectReference>` in a `.csproj`) is how one project
uses another. Core references nothing, which is why it is so easy to test.

**Exercise:** open `src/BoxBox.Api/BoxBox.Api.csproj` and find the line that makes the API use Core.

## 2. Records and immutability (Core)

`src/BoxBox.Core/Models/FuelEntry.cs` is a `record`: a compact type with value equality and `with` copies.
Tests build modified copies with `entry with { Litres = 25m }`.

**Exercise:** add an optional `string? Station` to `FuelEntry`. What breaks? (Nothing should, because it is optional.)

## 3. LINQ and pure functions (Core)

`src/BoxBox.Core/Analytics/SpendSummary.cs` groups, sums and averages with LINQ. It takes data in and returns a
result, with no side effects, so it is trivially testable.

**Exercise:** add "most expensive fill-up" to `SpendSummaryResult` and a test for it in `SpendSummaryTests.cs`.

## 4. Unit testing with xUnit

`tests/BoxBox.Tests/FuelEconomyTests.cs`: `[Fact]` is one test, `[Theory]` runs the same test over many inputs
(see `Confidence_grows_with_history` in `SpendForecasterTests.cs`). A good workflow: write the test first, watch it fail, then make
it pass.

**Exercise:** write a failing test for "two fill-ups on the same day count as one fill-up in the forecast", then
decide whether you actually want that behaviour.

## 5. Minimal APIs and dependency injection (Api)

`src/BoxBox.Api/Program.cs` maps URLs to lambdas (`app.MapGet("/api/summary", ...)`). Parameters such as
`BoxBoxDb db` are supplied by **dependency injection**: the framework builds them from what was registered with
`builder.Services`.

**Exercise:** add `GET /api/entries/count` returning the number of entries.

## 6. EF Core and SQLite

`BoxBoxDb` (bottom of `Program.cs`) is a `DbContext`; `DbSet<EntryRow>` is a table. `async`/`await` on
`ToListAsync()` frees the thread while the database works. Note the schema-upgrade block at startup: a simple
alternative to migrations.

**Exercise:** read about EF Core migrations, then decide whether this app should replace the upgrade block.

## 7. Shared contracts

`src/BoxBox.Core/Contracts/EntryContracts.cs` holds the request/response types used by BOTH the API and the web
app. Rename a field and the build breaks in both places until they agree. That is the main reason to use Blazor
with a .NET API.

## 8. Blazor components (Web)

Start with the smallest: `src/BoxBox.Web/Components/Stat.razor` (a component with two `[Parameter]` inputs), then
`PriceCard.razor` (uses `Stat` three times), then `Pages/Home.razor` (owns the data, passes it down).

Key ideas, each with an example:
- **Parameters down, events up:** `[Parameter]` in `HistoryList.razor`; `EventCallback` for "delete was clicked".
- **Forms and validation:** `AddEntryForm.razor` uses `EditForm`, `InputNumber` and DataAnnotations attributes.
- **Lifecycle:** `OnInitializedAsync` in `Home.razor` loads data when the page appears.
- **Services and DI in the browser:** `Services/BoxBoxApi.cs`, registered in `Program.cs`, injected with `@inject`.
- **JavaScript interop:** `HistoryList.razor` calls the browser's `confirm()` dialog.

**Exercise:** add a "Cost per litre" `Stat` to `LitresCard.razor`.

## 9. Testing components with bUnit

`tests/BoxBox.Tests/BlazorComponentTests.cs` renders components in memory and checks the HTML. A fake
`HttpMessageHandler` plays the server, and builds its JSON from the real Core types.

**Exercise:** add a test that `HistoryList` shows at most 50 rows.

## 10. Serving and deploying

`src/BoxBox.Api/StaticAssets.cs` is custom **middleware**: code that sits in the request pipeline and can change
a request or response. It serves pre-compressed files and sets cache headers. `deploy/deploy.sh` builds, tests,
publishes and ships it all.

**Exercise:** make `/api/summary` send a `Cache-Control: no-store` header using a tiny middleware or endpoint filter.

## Next steps once these feel easy
- Replace the schema-upgrade block with EF Core migrations.
- Add a service worker for offline entry (queue in IndexedDB, sync later).
- Add login (ASP.NET Core Identity or a reverse-proxy login) before exposing it beyond a private network.
- Try ML.NET once there are hundreds of fill-ups; `SpendForecaster`'s backtest is the yardstick it must beat.
