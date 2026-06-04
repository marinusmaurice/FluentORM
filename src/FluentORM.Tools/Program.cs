using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Mapping;
using FluentORM.Migrations.Drift;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Scaffold;
using FluentORM.Migrations.Schema;

namespace FluentORM.Tools;

/// <summary>
/// FluentORM CLI tool.
/// Usage: dotnet fluentorm migrations [command] [options]
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        // Dispatch on "migrations <command>"
        if (args[0].Equals("migrations", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
        {
            return await RunMigrationsCommandAsync(args[1..]);
        }

        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintHelp();
        return 1;
    }

    private static async Task<int> RunMigrationsCommandAsync(string[] args)
    {
        var command = args[0].ToLower();
        var opts = ParseOptions(args[1..]);

        var (engine, detector, generator) = BuildEngine(opts);
        if (engine == null)
        {
            Console.Error.WriteLine("ERROR: Could not build migration engine. " +
                "Ensure --connection is provided and the target assembly is loadable.");
            return 1;
        }

        switch (command)
        {
            case "status":
                return await RunStatusAsync(engine!);

            case "apply":
                return await RunApplyAsync(engine!, opts.ContainsKey("allow-destructive"));

            case "rollback":
                if (opts.TryGetValue("to", out var toVersion))
                    await engine!.RollbackToAsync(long.Parse(toVersion));
                else
                    await engine!.RollbackAsync();
                Console.WriteLine("Rollback complete.");
                return 0;

            case "preview":
                var preview = await engine!.PreviewAsync();
                Console.WriteLine(preview);
                return 0;

            case "list":
                return await RunListAsync(engine!);

            case "history":
                return await RunHistoryAsync(engine!);

            case "validate":
                return await RunValidateAsync(engine!, detector, opts.ContainsKey("check-checksums"));

            case "scaffold":
                if (generator == null)
                {
                    Console.Error.WriteLine("ERROR: Scaffold requires --connection and a loaded assembly.");
                    return 1;
                }
                var desc = args.Length > 1 ? args[1] : "auto_migration";
                var output = opts.TryGetValue("output", out var outDir) ? outDir : Directory.GetCurrentDirectory();
                var dryRun = opts.ContainsKey("dry-run");
                var result = await generator.GenerateAsync(desc, dryRun ? null : output, dryRun);
                Console.WriteLine(result);
                return 0;

            default:
                Console.Error.WriteLine($"Unknown migrations command: {command}");
                PrintMigrationsHelp();
                return 1;
        }
    }

    private static async Task<int> RunStatusAsync(MigrationEngine engine)
    {
        var status = await engine.StatusAsync();

        Console.WriteLine();
        Console.WriteLine("FluentORM Migration Status");
        Console.WriteLine(new string('═', 58));
        Console.WriteLine($"  Applied      : {status.Applied.Count,3} migration(s)");
        Console.WriteLine($"  Pending      : {status.Pending.Count,3} migration(s)");

        if (status.DestructivePending.Count > 0)
            Console.WriteLine($"  Destructive  : {status.DestructivePending.Count,3} migration(s)  ⚠  (requires --allow-destructive)");

        if (status.Pending.Count > 0 || status.DestructivePending.Count > 0)
        {
            Console.WriteLine();
            foreach (var m in status.Pending)
                Console.WriteLine($"  PENDING  {m.Version}  {m.Description,-40} [safe]");
            foreach (var m in status.DestructivePending)
            {
                Console.WriteLine($"  PENDING  {m.Version}  {m.Description,-40} [DESTRUCTIVE]");
                if (m.DestructiveReason != null)
                    Console.WriteLine($"             └─ '{m.DestructiveReason}'");
            }
        }

        Console.WriteLine();
        if (status.Pending.Count > 0 || status.DestructivePending.Count > 0)
        {
            Console.WriteLine($"  Run: dotnet fluentorm migrations apply");
            if (status.DestructivePending.Count > 0)
                Console.WriteLine($"  Run: dotnet fluentorm migrations apply --allow-destructive");
        }
        Console.WriteLine(new string('═', 58));
        return 0;
    }

    private static async Task<int> RunApplyAsync(MigrationEngine engine, bool allowDestructive)
    {
        try
        {
            var beforeStatus = await engine.StatusAsync();
            var pendingCount = beforeStatus.Pending.Count + beforeStatus.DestructivePending.Count;

            if (pendingCount == 0)
            {
                Console.WriteLine("No pending migrations.");
                return 0;
            }

            Console.WriteLine($"Applying {pendingCount} pending migration(s)...");
            await engine.ApplyAsync(allowDestructive);
            Console.WriteLine("✓ All migrations applied successfully.");
            return 0;
        }
        catch (Core.Exceptions.DestructiveMigrationException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.Error.WriteLine("Re-run with --allow-destructive to proceed.");
            return 2;
        }
        catch (Core.Exceptions.MigrationTamperedWithException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunListAsync(MigrationEngine engine)
    {
        var status = await engine.StatusAsync();
        Console.WriteLine($"{"Version",-20} {"Description",-40} {"Status",-12} {"Applied At",-24}");
        Console.WriteLine(new string('-', 100));
        foreach (var m in status.Applied)
            Console.WriteLine($"{m.Version,-20} {m.Description,-40} {"Applied",-12} {m.AppliedAt:yyyy-MM-dd HH:mm:ss}");
        foreach (var m in status.Pending)
            Console.WriteLine($"{m.Version,-20} {m.Description,-40} {"Pending",-12}");
        foreach (var m in status.DestructivePending)
            Console.WriteLine($"{m.Version,-20} {m.Description,-40} {"DESTRUCTIVE",-12}");
        return 0;
    }

    private static async Task<int> RunHistoryAsync(MigrationEngine engine)
    {
        var status = await engine.StatusAsync();
        Console.WriteLine("Applied migration history:");
        Console.WriteLine(new string('-', 80));
        foreach (var m in status.Applied.OrderBy(a => a.Version))
            Console.WriteLine($"  {m.Version}  {m.Description,-40}  applied {m.AppliedAt:yyyy-MM-dd HH:mm}");
        return 0;
    }

    private static async Task<int> RunValidateAsync(
        MigrationEngine engine, SchemaDriftDetector? detector, bool checkChecksums)
    {
        if (checkChecksums)
        {
            try
            {
                await engine.ValidateChecksumsAsync();
                Console.WriteLine("✓ All checksums valid — no migrations tampered with.");
            }
            catch (Core.Exceptions.MigrationTamperedWithException ex)
            {
                Console.Error.WriteLine($"✗ TAMPERED: {ex.Message}");
                return 1;
            }
        }

        if (detector != null)
        {
            var report = await detector.DetectAsync();
            if (report.Issues.Count == 0)
            {
                Console.WriteLine("✓ No schema drift detected.");
            }
            else
            {
                Console.WriteLine(report.ToString());
                return report.HasErrors ? 1 : 0;
            }
        }
        return 0;
    }

    private static (MigrationEngine? engine, SchemaDriftDetector? detector, ScaffoldGenerator? generator)
        BuildEngine(System.Collections.Generic.Dictionary<string, string> opts)
    {
        // In a real CLI tool, these would be loaded from config or env vars.
        // For library use, the engine is instantiated by user code.
        // This is a placeholder that demonstrates the wiring.
        return (null, null, null);
    }

    private static System.Collections.Generic.Dictionary<string, string> ParseOptions(string[] args)
    {
        var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i][2..];
                var val = (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    ? args[++i]
                    : "true";
                dict[key] = val;
            }
        }
        return dict;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("FluentORM CLI tool");
        Console.WriteLine("Usage: dotnet fluentorm migrations <command> [options]");
        Console.WriteLine();
        PrintMigrationsHelp();
    }

    private static void PrintMigrationsHelp()
    {
        Console.WriteLine("Migration commands:");
        Console.WriteLine("  status                    Show applied, pending, and destructive-pending migrations");
        Console.WriteLine("  apply                     Apply all safe pending migrations");
        Console.WriteLine("  apply --allow-destructive Apply all pending including destructive");
        Console.WriteLine("  apply --to <version>      Apply up to specific version");
        Console.WriteLine("  rollback                  Roll back the last applied migration");
        Console.WriteLine("  rollback --to <version>   Roll back to specific version");
        Console.WriteLine("  preview                   Show SQL that would be executed, no DB changes");
        Console.WriteLine("  list                      List all migration classes found in assembly");
        Console.WriteLine("  history                   Show full applied migration history from DB");
        Console.WriteLine("  validate                  Run schema drift detection and report");
        Console.WriteLine("  validate --check-checksums Verify no applied migrations were tampered with");
        Console.WriteLine("  scaffold \"description\"    Generate migration file from detected drift");
        Console.WriteLine("  scaffold --dry-run        Preview scaffold output without writing file");
        Console.WriteLine("  scaffold --output <dir>   Write to specific directory");
    }
}
