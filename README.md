# BoxBox

A small, self-hosted **fuel spending tracker** for your own car. Type in what you paid and how
many litres you pumped; BoxBox shows what you spend per month and what you pay per litre.

It is a single-user web app that installs on your phone's home screen (PWA). Your data lives in
one SQLite file that you own.

> **Status:** early. The dashboard, API, CSV import and deployment script work. Offline entry,
> authentication and maintenance tracking are not built yet (see [Roadmap](#roadmap)).

## Features

- **Quick entry:** amount paid (RM) and litres; date defaults to today. Price per litre is calculated for you.
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
cd boxbox/src/BoxBox.Api
dotnet run
```

Open the URL printed in the console (look for `Now listening on:`). A `boxbox.db` file is
created next to the app on first run.

Run the tests from the repository root:

```sh
dotnet test
```

## Configuration

| Setting | How | Default |
|---|---|---|
| Listen address | `ASPNETCORE_URLS` environment variable | with `dotnet run`, the port from `launchSettings.json` (printed on start) |
| Database file | `ConnectionStrings__BoxBox` environment variable | `Data Source=<app folder>/boxbox.db` |

Example:

```sh
ASPNETCORE_URLS=http://127.0.0.1:5080 \
ConnectionStrings__BoxBox="Data Source=/var/lib/boxbox/boxbox.db" \
dotnet run --no-launch-profile
```

Dates use Malaysia time (`Asia/Kuala_Lumpur`) and amounts are in RM. The currency label is in
`src/BoxBox.Api/wwwroot/index.html` if you want to change it.

## Importing past records

In the app, open **Import past amounts**, or call the API directly:

```sh
curl -X POST http://localhost:PORT/api/import \
  -H 'Content-Type: text/plain' \
  --data-binary $'date,amount,litres\n2026-03-14,60\n2026-03-28,55.40,27.1\n'
```

- One record per line: `date,amount` or `date,amount,litres`.
- Dates: `2026-03-14` or `14/03/2026`.
- A header line and blank lines are skipped.
- All-or-nothing: if any line is invalid, nothing is imported and the errors are returned.

## API

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/entries` | List entries, newest first |
| `POST` | `/api/entries` | Add `{ "amountPaid": 60, "litres": 29.3, "date": "2026-11-18" }` (`litres` and `date` optional) |
| `DELETE` | `/api/entries/{id}` | Delete an entry |
| `GET` | `/api/summary` | Totals, monthly breakdown, averages |
| `POST` | `/api/import` | CSV text import (see above) |

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
release, uploads it to a Linux server over SSH, switches a `current` symlink, restarts the service,
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
src/BoxBox.Api/    Minimal API, EF Core + SQLite, and the static dashboard (wwwroot/)
tests/BoxBox.Tests xUnit tests
deploy/            Example systemd unit and deployment script
```

`BoxBox.Core` also contains analytics that need an odometer reading (full-tank consumption in
L/100 km, cost per km, robust anomaly flags, next-fill-up forecast). They are tested but not wired
into the dashboard yet.

## Roadmap

- Offline entry with sync when the connection returns
- Optional odometer, then fuel-consumption trends, anomaly hints and a next-fill-up forecast
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
