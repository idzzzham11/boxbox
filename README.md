# BoxBox

A small, self-hosted **fuel spending tracker** for your own car. Type in what you paid and how
many litres you pumped; BoxBox shows what you spend per month and what you pay per litre.

It is a single-user web app that installs on your phone's home screen (PWA). Your data lives in
one SQLite file that you own.

> **Status:** early. The dashboard, API, CSV import and deployment script work. Offline entry,
> authentication and maintenance tracking are not built yet (see [Roadmap](#roadmap)).

## Features

- **Quick entry:** amount paid (RM) and litres; date defaults to today. Price per litre is calculated for you.
- **Forecast:** next fill-up date, amount and litres, plus projected spend for the month, learned from your
  own history (see [Forecasting](#forecasting)).
- **Fuel economy without an odometer:** reset your trip meter at every fill-up and type in the trip km next
  time. BoxBox shows L/100 km, km/L and cost per km (see below).
- **Dashboard:** this month, last month, average per month, average price per litre, total spent, total litres, spending by month.
- **History:** recent fill-ups with delete.
- **Import old records:** paste `date,amount` or `date,amount,litres` lines. Litres are optional, so records
  where you only remember the amount still count towards spending.
- **Installable:** web manifest and icons, so it can be added to a phone home screen.
- **Small:** ASP.NET Core minimal API, EF Core + SQLite, one static HTML page. Runs comfortably in
  0.5 vCPU / 1 GB RAM.
- **Tested analytics:** a separate `BoxBox.Core` library holds the pure logic (spend summaries, validation,
  full-tank fuel consumption, anomaly detection, fill-up forecast) with xUnit tests.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build and run from source.
  (A self-contained publish needs no .NET on the server.)

## Quick start

```sh
git clone https://github.com/idzzzham11/boxbox.git
cd boxbox
./scripts/run-local.sh
```

Open http://localhost:5080. This builds the API and the Blazor front end together and keeps your data in
`local-data/boxbox.db`. To run just the JSON API (no web pages) use `dotnet run` in `src/BoxBox.Api`.

Run the tests from the repository root:

```sh
dotnet test
```

## Configuration

| Setting | How | Default |
|---|---|---|
| Listen address | `ASPNETCORE_URLS` environment variable | with `dotnet run`, the port from `launchSettings.json` (printed on start) |
| Database file | `ConnectionStrings__BoxBox` environment variable | `Data Source=<app folder>/boxbox.db` |
| Pump price for litre estimates | `FuelPrices:Budi95` in `appsettings.json` | RM1.99 from 2025-09-30 |

Example:

```sh
ASPNETCORE_URLS=http://127.0.0.1:5080 \
ConnectionStrings__BoxBox="Data Source=/var/lib/boxbox/boxbox.db" \
dotnet run --no-launch-profile
```

Dates use Malaysia time (`Asia/Kuala_Lumpur`) and amounts are in RM. The currency label is in
`src/BoxBox.Api/wwwroot/index.html` if you want to change it.

## Estimating litres from the amount paid

Litres are optional. If you leave them out, BoxBox estimates them as `amount / price`, using the pump price
in force on that date (default: the subsidised BUDI95 RON95 price, RM1.99/L since 2025-09-30). Estimated
litres are shown as `≈ 30.12 L est.` and counted in the litre total, but:

- Litres you record always win over the estimate.
- The average price per litre is calculated only from litres you really recorded, never from estimates.
- It is only an estimate. Pumps round, and if you pay a different price (for example above the subsidy quota,
  or an unsubsidised grade) the estimate is wrong. Enter the real litres in those cases.

Set the price in `src/BoxBox.Api/appsettings.json` (add a new entry whenever the price changes; the latest
`From` date on or before a fill-up wins). Remove the entries to turn estimation off.

```json
"FuelPrices": { "Budi95": [ { "From": "2025-09-30", "PerLitre": 1.99 } ] }
```

## Forecasting

`GET /api/forecast` (and the Forecast card) predict the next fill-up's date, amount and litres, and the month's
total spend. It needs at least 6 fill-ups.

One car produces very little data, so this is deliberately **not** a neural network or a library model: there
are not enough points to train one without it memorising noise. Instead, for each signal (days between
fill-ups, RM per fill-up, litres per fill-up) BoxBox tries four small models (last value, average of all,
median of the last 6, exponential smoothing), scores each with a rolling backtest (train on the first k
values, predict value k, repeat) and uses the one with the lowest error.

- The naive guess, "same as last time", is always scored too, and the result says whether the chosen model
  actually beats it.
- The ranges shown are the typical backtest error of the chosen model, not a statistical guarantee.
- Confidence is labelled from the amount of history (very low under 8 fill-ups, low under 20, moderate under 40).
- If all litres are estimated from the price, the litres forecast is just the amount forecast divided by the
  price and adds no information.
- Pump prices are not forecast: the BUDI95 price is set by policy, not by a pattern in your data.

The code is in `BoxBox.Core/Analytics/SpendForecaster.cs`. Once there are a few hundred fill-ups, a model
library such as ML.NET becomes worth trying, and the backtest in this file is the yardstick it would have to beat.

## How fuel economy is calculated

Reset your trip meter after every fill-up, and fill the tank to the pump's automatic cut-off each time. At
the next fill-up, enter the trip-meter km together with the litres and amount.

- Each fill-up with litres (recorded or estimated) and trip km is one measurement: the litres you put in replace the fuel burnt over
  that trip distance.
- **The first complete fill-up only sets the starting point.** Its starting tank level is unknown, so the
  first figure appears after your second fill-up with litres and trip km entered.
- Averages are weighted by distance (total litres / total km), not an average of per-tank ratios.
- Records with litres (and no price to estimate them from) or trip km missing (for example old amount-only history) are never guessed. They
  break the chain, and the next complete fill-up becomes a new starting point.
- Topping up part-way breaks the assumption: that fill-up reads too low and the next one too high, though
  the long-run average stays close.

## Importing past records

In the app, open **Import past amounts**, or call the API directly:

```sh
curl -X POST http://localhost:PORT/api/import \
  -H 'Content-Type: text/plain' \
  --data-binary $'date,amount,litres,tripkm\n2026-03-14,60\n2026-03-28,55.40,27.1,410\n'
```

- One record per line: `date,amount[,litres[,trip km]]`.
- Dates: `2026-03-14` or `14/03/2026`.
- A header line and blank lines are skipped.
- All-or-nothing: if any line is invalid, nothing is imported and the errors are returned.

## API

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/entries` | List entries, newest first |
| `POST` | `/api/entries` | Add `{ "amountPaid": 60, "litres": 29.3, "tripKm": 410, "date": "2026-11-18" }` (everything except `amountPaid` optional) |
| `DELETE` | `/api/entries/{id}` | Delete an entry |
| `GET` | `/api/summary` | Totals, monthly breakdown, averages and fuel economy |
| `GET` | `/api/forecast` | Next fill-up date / amount / litres and projected month spend |
| `POST` | `/api/import` | CSV text import (see above) |

## Front end

The app is a single **Blazor WebAssembly** web app (`src/BoxBox.Web`) served from `/`. It shares its request and
response types with the API through `BoxBox.Core`, and its components are tested with bUnit.

The Blazor files are not in the API project's source folder. They are built and combined with the API when you
publish, so to try everything locally use the helper script:

```sh
./scripts/run-local.sh          # builds both, serves on http://localhost:5080, data in ./local-data/
```

(`deploy/deploy.sh` does the same combination for a server.) The API serves the pre-compressed `.br`/`.gz` files
Blazor publishes and caches the fingerprinted runtime files for a year, so a phone downloads the runtime once.
The first load is a few MB; later loads are fast.

## Hosting it

BoxBox is one process serving both the API and the page, so any way of running a .NET app works.

1. **Publish** a self-contained build (no .NET needed on the server):

   ```sh
   dotnet publish src/BoxBox.Api -c Release -r linux-x64 --self-contained -o ./publish
   ```

   Use `linux-arm64` for ARM servers.
2. **Run it** behind something that provides **HTTPS**. Browsers only let you install a web app to the
   home screen over HTTPS (or `localhost`). Any of these work: a reverse proxy such as Caddy or nginx,
   a tunnel service, or a private network/VPN with HTTPS certificates. Keep the app itself bound to
   `127.0.0.1` and let the proxy face the network.
3. **Keep it running** with your usual tool, such as a systemd service. See
   [`deploy/boxbox.service`](deploy/boxbox.service) for an example unit.
4. **Back up** the SQLite file regularly (copy it, or use `sqlite3 boxbox.db ".backup backup.db"`).

[`deploy/deploy.sh`](deploy/deploy.sh) is an optional example script. It runs the tests, builds a
release (including the Blazor front end), uploads it to a Linux server over SSH, switches a `current` symlink, restarts the service,
health-checks it and rolls back on failure. It never touches the database.

```sh
DEPLOY_HOST=root@my-server.example.com ARCH=linux-x64 ./deploy/deploy.sh
```

Read it before use: it installs a systemd unit on the target and expects `rsync` and `curl` there.

### Security: there is no login

BoxBox v1 has **no authentication**. Anyone who can reach the URL can read, add and delete data.
Only expose it on a network you trust (localhost, a VPN, a private network). Do not put it on the
public internet until authentication is added.

## Project layout

```
src/BoxBox.Core/   Domain models, validation and analytics (no database, easy to test)
src/BoxBox.Api/    Minimal API, EF Core + SQLite; serves both front ends
src/BoxBox.Web/    Blazor WebAssembly front end (the main app at /)
tests/BoxBox.Tests xUnit + bUnit tests
docs/              LEARN-DOTNET.md, a guided tour of the code with exercises
deploy/            Example systemd unit and deployment script
```

`BoxBox.Core` also contains analytics that need an odometer reading (robust anomaly flags, next-fill-up
forecast). They are tested but not wired into the dashboard yet.

## Roadmap

- Offline entry with sync when the connection returns (service worker + local queue)
- Consumption trend chart and anomaly hints
- Optional login
- Backup / restore and CSV export in the UI
- More than one vehicle
- Maintenance records and service reminders

Contributions and ideas are welcome. Open an issue first for larger changes.

## Contributing

1. Fork and create a branch.
2. `dotnet test` must pass; add tests for new logic in `BoxBox.Core`.
3. Open a pull request describing the change.

## License

[MIT](LICENSE)
