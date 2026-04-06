# db-migrator

A reusable CLI tool to migrate a PostgreSQL database from one server to another and declaratively sanitize sensitive data post-restore. Runs locally, in Docker, or as a Cloud Run Job.

## Features

- **Migrate** — `pg_dump` + `pg_restore` between any two Postgres instances
- **Parallel migration** — `parallel_jobs: N` for faster dump/restore on large databases
- **Live progress** — spinner with real-time per-table output and elapsed time during dump/restore
- **Sanitize** — declarative YAML rules targeting specific tables and columns
- **Pre-execution validation** — checks tables/columns exist, NOT NULL constraints, PK warnings — all against the live source DB
- **All-or-nothing** — sanitization runs inside a single transaction; rolls back completely on any error
- **Strategies** — `static`, `null_value`, `template`, `faker`, `hash`
- **Dry-run mode** — shows what would execute without touching data
- **Environment variable interpolation** — `${SECRET}` in config values

## Quick Start

### Local

```bash
# 1. Copy and edit config
cp config.example.yaml config.yaml

# 2. Validate config against source DB (no changes made)
SOURCE_DB_PASSWORD=... TARGET_DB_PASSWORD=... \
  dotnet run --project src/DbMigrator -- validate --config config.yaml

# 3. Run full pipeline (migrate + sanitize)
SOURCE_DB_PASSWORD=... TARGET_DB_PASSWORD=... \
  dotnet run --project src/DbMigrator -- run --config config.yaml
```

### Docker

```bash
docker build -t db-migrator .

docker run --rm \
  -e SOURCE_DB_PASSWORD=... \
  -e TARGET_DB_PASSWORD=... \
  -v $(pwd)/config.yaml:/app/config.yaml:ro \
  db-migrator run --config /app/config.yaml
```

### Docker Compose (with test databases)

```bash
# Starts source-db, target-db, and migrator
docker compose up --build
```

### Cloud Run Job

```bash
gcloud run jobs create db-migrator \
  --image gcr.io/my-project/db-migrator:latest \
  --set-secrets SOURCE_DB_PASSWORD=source-db-pass:latest \
  --set-secrets TARGET_DB_PASSWORD=target-db-pass:latest \
  --args="run,--config,/secrets/config.yaml"
```

## CLI Commands

| Command | Description |
|---|---|
| `run` | Full pipeline: migrate + sanitize |
| `migrate` | Only pg_dump + pg_restore |
| `sanitize` | Only sanitize (target DB must already exist) |
| `validate` | Validate config + check schema — no changes |

**Flags available on all commands:**

| Flag | Default | Description |
|---|---|---|
| `-c / --config` | `config.yaml` | Path to YAML config |
| `--dry-run` | false | Show what would run, execute nothing |
| `--skip-validation` | false | Skip pre-execution schema checks |
| `--strict` | false | Treat warnings as errors |
| `--log-level` | `info` | `debug` / `info` / `warn` / `error` |

## Configuration

```yaml
migration:
  source:
    host: "source-db.example.com"
    port: 5432
    database: "mydb"
    user: "postgres"
    password: "${SOURCE_DB_PASSWORD}"   # env var interpolation
    schema: "public"                    # schema for validation queries
    ssl_mode: "Prefer"                  # Disable | Allow | Prefer | Require | VerifyCA | VerifyFull
  target:
    host: "target-db.example.com"
    port: 5432
    database: "mydb_sanitized"
    user: "postgres"
    password: "${TARGET_DB_PASSWORD}"
  dump:
    exclude_tables: ["audit_logs", "sessions"]
    schema_only: false
    extra_pg_dump_args: []
    parallel_jobs: 1                  # optional: >1 enables -j N with directory format

sanitize:
  - table: users
    where: "role != 'admin'"            # optional WHERE clause
    columns:
      - name: email
        strategy: template
        value: "user_{id}@redacted.local"

      - name: first_name
        strategy: faker
        faker: first_name

      - name: password_hash
        strategy: static
        value: "$2b$10$REDACTED"

      - name: national_id
        strategy: hash                  # SHA256, deterministic

      - name: avatar_url
        strategy: null_value            # SET NULL
```

## Sanitization Strategies

| Strategy | Description | Config |
|---|---|---|
| `static` | Fixed value for all rows | `value: "..."` |
| `null_value` | SET NULL | — |
| `template` | Value with `{column}` references | `value: "user_{id}@x.com"` |
| `faker` | Realistic fake data via [Bogus](https://github.com/bchavez/Bogus) | `faker: first_name` |
| `hash` | SHA-256 of original value (deterministic) | — |

### Available faker methods

`first_name` · `last_name` · `full_name` · `email` · `user_name` · `phone` ·
`address` · `street_address` · `city` · `country` · `zip_code` · `company` ·
`lorem` · `paragraph` · `url` · `ip_address` · `uuid` · `random_number` ·
`random_double` · `digits_as_integer` · `date` · `datetime` · `color` ·
`product` · `price` · `iban` · `credit_card` · `ssn`

## Pre-execution Validation

Before any migration or sanitization, `validate` (or `run`) connects to the **source DB** and checks:

| Check | Severity |
|---|---|
| Table does not exist | **Error** |
| Column does not exist | **Error** |
| `null_value` on NOT NULL column | **Error** |
| `static: null` on NOT NULL column | **Error** |
| Table name / column name has invalid characters | **Error** (config level) |
| Source and target are the same database | **Error** (config level) |
| Table exists but is empty | Warning |
| Column is a PRIMARY KEY | Warning |
| Faker method may produce wrong type for numeric column | Warning |

Use `--strict` to promote warnings to errors. Use `--skip-validation` to bypass (with caution).

## Stack

- **.NET 10** — C#
- **Spectre.Console.Cli** — CLI framework
- **Npgsql** — PostgreSQL driver
- **YamlDotNet** — YAML config parsing
- **Bogus** — fake data generation
- **Serilog** — structured logging

## Running Tests

```bash
# All tests (unit + integration)
# Integration tests require Docker running (Testcontainers spins up Postgres automatically)
dotnet test

# Unit tests only (no Docker needed)
dotnet test --filter "FullyQualifiedName!~Integration"

# With coverage report
dotnet test --collect:"XPlat Code Coverage"
```

**Coverage: ~58% lines / 66% branches**

| Layer | Coverage |
|---|---|
| Config parsing & validation | 92–100% |
| All sanitization strategies | 94–100% |
| Sanitizer orchestration | 100% |
| Schema validator | 100% |
| Migrator arg builders | 100% |
| CLI commands / Program | 0% (E2E scope) |

## Project Structure

```
src/DbMigrator/
├── Config/            — YAML models + loader + structural validation
├── Validation/        — SchemaValidator (checks live source DB)
├── Migration/         — pg_dump / pg_restore subprocess wrapper
├── Sanitization/
│   ├── Sanitizer.cs   — orchestrator, transactional
│   └── Strategies/    — static, null_value, template, faker, hash
├── Commands/          — run / migrate / sanitize / validate
└── Infrastructure/    — DI bridge for Spectre.Console

tests/DbMigrator.Tests/
├── Config/            — ConfigLoader unit tests
├── Migration/         — pg_dump/pg_restore arg builder tests
├── Sanitization/      — strategy unit tests + SQL builder tests
├── Validation/        — ValidationResult unit tests
└── Integration/       — SchemaValidator + Sanitizer against real Postgres
                         (Testcontainers, no setup required)
```

## Security Notes

- Passwords are passed via environment variables, never in the config file on disk
- Table and column names are validated against `^[a-zA-Z_][a-zA-Z0-9_]*$` before use in SQL
- The dump file is stored in a system temp directory and deleted immediately after restore
- The Docker image runs as a non-root user (`uid 1001`)
- `WHERE` clauses in sanitize rules come from a trusted config file — do not expose config editing to untrusted users

## Documentation

Full documentation is available in the [GitHub Wiki](../../wiki):

| Page | Description |
|---|---|
| [Getting Started](../../wiki/Getting-Started) | Installation, requirements, first run |
| [Configuration](../../wiki/Configuration) | Full `config.yaml` reference |
| [CLI Commands](../../wiki/CLI-Commands) | All commands and flags |
| [Sanitization Strategies](../../wiki/Sanitization-Strategies) | How each strategy works |
| [Validation](../../wiki/Validation) | Pre-execution checks explained |
| [Docker Usage](../../wiki/Docker-Usage) | Docker and docker-compose setup |
| [Faker Methods Reference](../../wiki/Faker-Methods-Reference) | All available faker methods |

The wiki source lives in `docs/wiki/` (git submodule pointing to the GitHub Wiki repo).

## License

MIT
