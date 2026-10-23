# Database upgrades

## SQLite (legacy `EnsureCreated` databases)

Run the maintenance executable offline, against a copy or with the application stopped:

```bash
dotnet run --project HoaSap.Maintenance -- \
  --provider sqlite --connection 'Data Source=/srv/hoasap.db'
```

The default is a **dry run**. The plan only creates missing tables/indexes and adds missing columns. It never drops or rebuilds objects. Existing columns, foreign keys, unexpected tables, and conflicting indexes are refused with an actionable message.

To apply a reviewed plan, an explicit backup path is mandatory:

```bash
dotnet run --project HoaSap.Maintenance -- \
  --provider sqlite --connection 'Data Source=/srv/hoasap.db' \
  --apply --backup /srv/backups/hoasap-before-upgrade.db
```

The tool uses SQLite `BackupDatabase` before any mutation, applies additive statements in a transaction, validates `foreign_key_check` and `integrity_check`, and verifies row counts are unchanged. Re-running it is idempotent. An incompatible schema is refused; the backup is still produced for an apply invocation before the refusal is reported. Do not point this command at a live production database while the API is writing.

## SQL Server

Do not auto-run schema changes at application startup. Generate and review the EF Core migration first, then deploy an idempotent script through the normal maintenance/change-control process:

```bash
dotnet ef migrations script --idempotent \
  --project HoaSap.DAL --startup-project HoaSap.API \
  --context HoaSapDbContext --output HoaSap.DAL/Migrations/sqlserver-deploy.sql
```

An unversioned SQL Server database must **not** be blindly stamped into `__EFMigrationsHistory`. Compare its schema and data, take a backup, and use a reviewed, explicit migration (including any backfill) before recording history. The SQLite upgrader is not a SQL Server migration mechanism.
