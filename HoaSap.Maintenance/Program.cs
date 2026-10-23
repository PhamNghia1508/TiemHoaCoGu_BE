using HoaSap.DAL;
using HoaSap.DAL.Maintenance;
using Microsoft.EntityFrameworkCore;

if (!args.Contains("--provider", StringComparer.OrdinalIgnoreCase) ||
    !string.Equals(Value("--provider"), "sqlite", StringComparison.OrdinalIgnoreCase))
{ Console.Error.WriteLine("Usage: HoaSap.Maintenance --provider sqlite --connection <path-or-connection-string> [--apply --backup <path>]"); return 2; }
var connection = Value("--connection") ?? throw new ArgumentException("--connection is required.");
var backup = Value("--backup");
var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
var options = new DbContextOptionsBuilder<HoaSapDbContext>().UseSqlite(connection).Options;
var plan = await SqliteSchemaUpgrade.UpgradeAsync(options, new SqliteUpgradeOptions(apply, backup));
Console.WriteLine(plan);
if (!plan.CanApply) return 3;
if (!apply && plan.HasChanges) Console.WriteLine("Dry run only. Re-run with --apply --backup <offline-copy-path> to mutate the database.");
return 0;

string? Value(string key)
{
    var i = Array.FindIndex(args, x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
