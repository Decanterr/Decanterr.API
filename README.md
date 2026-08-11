# Decanterr API

A headless, self-hosted ASP.NET Core backend for managing an audiobook library — built on top of [Libation](https://github.com/rmcrackan/Libation), exposed as a REST + SignalR API for the [Decanterr.Web](../Decanterr.Web) frontend.

## Tech stack

- ASP.NET Core (net10.0) Web API + SignalR
- PostgreSQL (via `DataLayer.Postgres`)
- Vendored Libation core libraries (`AaxDecrypter`, `FileLiberator`, `AudibleUtilities`, `LibationFileManager`, etc.) for account management, downloading, and library processing

## Getting started

```bash
dotnet restore
dotnet run --project Source/Decanterr.Api
```

Local secrets/overrides (API keys, Audiobookshelf token, books directory, etc.) belong in `Source/Decanterr.Api/appsettings.Development.json`, which is gitignored. `appsettings.json` should only ever contain placeholder/empty values.

## Configuration

Key settings (via `appsettings.json`, environment variables, or `appsettings.Development.json`):

| Setting                          | Description                                            |
| --------------------------------- | -------------------------------------------------------- |
| `ApiKeys`                         | List of accepted API keys for `X-Api-Key` auth            |
| `ConnectionStrings:Postgres`      | PostgreSQL connection string                              |
| `LibationFiles`                   | Directory for Libation's config/settings/database          |
| `BooksDirectory`                  | Where processed (downloaded) books are stored              |
| `Cors:Origins`                    | Allowed origins for the web frontend                       |
| `Audiobookshelf:Enabled/Url/ApiToken` | Optional sync target for uploading processed books        |

## Docker

```bash
docker compose -f docker-compose.api.yml up --build
```

Builds and runs the API, the web frontend (`../Decanterr.Web`), and a Postgres database together.

## API examples

All endpoints except `/api/health` require an `X-Api-Key` header matching one of the configured `ApiKeys`.

```bash
# Health check (no auth)
curl http://localhost:5000/api/health

# List accounts
curl http://localhost:5000/api/accounts \
  -H "X-Api-Key: CHANGE-ME-TO-A-SECURE-KEY"

# Search books
curl "http://localhost:5000/api/books/search?q=Mistborn" \
  -H "X-Api-Key: CHANGE-ME-TO-A-SECURE-KEY"

# Trigger a library scan for all accounts
curl -X POST http://localhost:5000/api/library/scan \
  -H "X-Api-Key: CHANGE-ME-TO-A-SECURE-KEY"

# Process (download) a book by ASIN, URL, or product ID
curl -X POST http://localhost:5000/api/liberate \
  -H "X-Api-Key: CHANGE-ME-TO-A-SECURE-KEY" \
  -H "Content-Type: application/json" \
  -d '{"input": "REPLACE_WITH_ASIN"}'

# Update a book's tags
curl -X PUT http://localhost:5000/api/books/REPLACE_WITH_ASIN/tags \
  -H "X-Api-Key: CHANGE-ME-TO-A-SECURE-KEY" \
  -H "Content-Type: application/json" \
  -d '{"tags": "fiction, fantasy"}'
```

For a full, ready-to-run set of requests (accounts, books, library, liberate, queue), see `Source/Decanterr.Api/api.http` (gitignored — it's a local scratchpad, open it with the VS Code REST Client extension) or the Swagger UI at `/swagger` when running the API.