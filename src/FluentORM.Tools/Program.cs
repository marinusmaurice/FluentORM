using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Exceptions;
using FluentORM.Core.Mapping;
using FluentORM.Migrations.Drift;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Scaffold;
using FluentORM.Migrations.Snapshot;
using FluentORM.Sqlite;
using FluentORM.SqlServer;

namespace FluentORM.Tools;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "init"       => RunInit(args[1..]),
            "migrations" => args.Length < 2 || args[1] is "-h" or "--help"
                                ? PrintMigrationsHelpAndReturn()
                                : await RunMigrationsCommandAsync(args[1..]),
            _ => ErrorExit($"Unknown command: {args[0]}")
        };
    }

    // ── migrations <command> ──────────────────────────────────────────────────

    private static async Task<int> RunMigrationsCommandAsync(string[] args)
    {
        var command = args[0].ToLowerInvariant();
        var (positional, opts) = ParseArgs(args[1..]);
        var config = LoadConfig(opts);

        // Commands that work without a live DB connection
        if (command == "new")
        {
            var desc = positional.FirstOrDefault() ?? opts.GetValueOrDefault("name") ?? "new_migration";
            var outDir = opts.GetValueOrDefault("output") ?? Directory.GetCurrentDirectory();
            return CreateBlankMigration(desc, outDir, config);
        }

        if (command == "scaffold")
        {
            // Snapshot mode only needs the assembly; no DB connection required.
            // --no-snapshot falls back to live-DB drift detection and DOES need a connection.
            var noSnap = opts.ContainsKey("no-snapshot");
            var assemblies = LoadAssemblies(config.Assembly);
            var registry   = BuildRegistry(assemblies);
            ScaffoldGenerator? dbGen = null;

            if (noSnap)
            {
                var (engine, detector, gen) = BuildEngine(config);
                if (engine is null)
                    return ErrorExit(
                        "--no-snapshot requires a valid --connection to run live DB drift detection.");
                dbGen = gen;
            }

            return await RunScaffoldAsync(dbGen, registry, config, positional, opts);
        }

        // All remaining commands need a live DB
        var (eng, det, generator) = BuildEngine(config);
        if (eng is null)
            return ErrorExit(
                "Could not build migration engine.\n" +
                "  Provide --connection and optionally --provider and --assembly,\n" +
                "  or run 'fluentorm init' to create a fluentorm.json config file.");

        return command switch
        {
            "status"   => await RunStatusAsync(eng),
            "apply"    => await RunApplyAsync(eng, opts),
            "rollback" => await RunRollbackAsync(eng, opts),
            "preview"  => await RunPreviewAsync(eng),
            "list"     => await RunListAsync(eng),
            "history"  => await RunHistoryAsync(eng),
            "validate" => await RunValidateAsync(eng, det, opts),
            _          => ErrorExit($"Unknown migrations command: {command}\n  Run 'fluentorm migrations --help' for a list of commands.")
        };
    }

    // ── init ─────────────────────────────────────────────────────────────────

    private static int RunInit(string[] args)
    {
        var (_, opts) = ParseArgs(args);
        var path = opts.GetValueOrDefault("config")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "fluentorm.json");

        if (File.Exists(path))
        {
            PrintWarn($"Config file already exists: {path}");
            return 1;
        }

        var template = new CliConfig
        {
            Provider = "sqlite",
            ConnectionString = "Data Source=myapp.db",
            Assembly = "./MyApp.dll",
            MigrationsNamespace = "MyApp.Migrations"
        };

        var json = JsonSerializer.Serialize(template, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(path, json);
        PrintOk($"Created {path}");
        Console.WriteLine("  Edit provider, connectionString, and assembly to match your project.");
        return 0;
    }

    // ── status ────────────────────────────────────────────────────────────────

    private static async Task<int> RunStatusAsync(MigrationEngine engine)
    {
        var status = await engine.StatusAsync();

        Banner("Migration Status");
        Console.WriteLine($"  {"Applied",-22} {status.Applied.Count,4}");
        Console.WriteLine($"  {"Pending (safe)",-22} {status.Pending.Count,4}");
        if (status.DestructivePending.Count > 0)
            Console.WriteLine($"  {"Pending (destructive)",-22} {status.DestructivePending.Count,4}  ← requires --allow-destructive");

        if (status.Applied.Any())
        {
            Console.WriteLine();
            Console.WriteLine("  Applied:");
            foreach (var m in status.Applied)
                Console.WriteLine($"    {Tick} {m.Version}  {m.Description,-40}  {m.AppliedAt:yyyy-MM-dd HH:mm}");
        }

        if (status.Pending.Any() || status.DestructivePending.Any())
        {
            Console.WriteLine();
            Console.WriteLine("  Pending:");
            foreach (var m in status.Pending)
                Console.WriteLine($"    {Dot} {m.Version}  {m.Description,-40}  [safe]");
            foreach (var m in status.DestructivePending)
            {
                Console.WriteLine($"    {Warn} {m.Version}  {m.Description,-40}  [DESTRUCTIVE]");
                if (m.DestructiveReason != null)
                    Console.WriteLine($"          └─ {m.DestructiveReason}");
            }

            Console.WriteLine();
            Console.WriteLine("  Run: fluentorm migrations apply");
            if (status.DestructivePending.Count > 0)
                Console.WriteLine("  Run: fluentorm migrations apply --allow-destructive");
        }
        else
        {
            Console.WriteLine();
            PrintOk("Database is up to date.");
        }

        Ruler();
        return 0;
    }

    // ── apply ─────────────────────────────────────────────────────────────────

    private static async Task<int> RunApplyAsync(MigrationEngine engine, Dictionary<string, string> opts)
    {
        bool allowDestructive = opts.ContainsKey("allow-destructive");

        if (opts.TryGetValue("to", out var toStr))
        {
            if (!long.TryParse(toStr, out var toVersion))
                return ErrorExit($"Invalid version: '{toStr}'. Version must be a number like 20240601001.");
            Console.WriteLine($"  Applying migrations up to version {toVersion}...");
            try
            {
                await engine.ApplyToAsync(toVersion, allowDestructive);
                PrintOk($"Applied all migrations up to version {toVersion}.");
                return 0;
            }
            catch (Exception ex) { return HandleMigrationException(ex); }
        }

        var before = await engine.StatusAsync();
        int pendingCount = before.Pending.Count + before.DestructivePending.Count;
        if (pendingCount == 0)
        {
            PrintOk("Database is up to date. Nothing to apply.");
            return 0;
        }

        Console.WriteLine($"  Applying {pendingCount} pending migration(s)...");
        try
        {
            await engine.ApplyAsync(allowDestructive);
            var after = await engine.StatusAsync();
            int applied = after.Applied.Count - before.Applied.Count;
            PrintOk($"Applied {applied} migration(s). Database is up to date.");
            return 0;
        }
        catch (Exception ex) { return HandleMigrationException(ex); }
    }

    // ── rollback ──────────────────────────────────────────────────────────────

    private static async Task<int> RunRollbackAsync(MigrationEngine engine, Dictionary<string, string> opts)
    {
        if (opts.TryGetValue("to", out var toStr))
        {
            if (!long.TryParse(toStr, out var toVersion))
                return ErrorExit($"Invalid version: '{toStr}'.");
            Console.WriteLine($"  Rolling back to version {toVersion}...");
            try { await engine.RollbackToAsync(toVersion); PrintOk("Rollback complete."); return 0; }
            catch (Exception ex) { return HandleMigrationException(ex); }
        }

        Console.WriteLine("  Rolling back last applied migration...");
        try { await engine.RollbackAsync(); PrintOk("Rollback complete."); return 0; }
        catch (Exception ex) { return HandleMigrationException(ex); }
    }

    // ── preview ───────────────────────────────────────────────────────────────

    private static async Task<int> RunPreviewAsync(MigrationEngine engine)
    {
        var sql = await engine.PreviewAsync();
        if (string.IsNullOrWhiteSpace(sql))
        {
            PrintOk("No pending migrations. Nothing to preview.");
            return 0;
        }
        Console.WriteLine(sql);
        return 0;
    }

    // ── list ──────────────────────────────────────────────────────────────────

    private static async Task<int> RunListAsync(MigrationEngine engine)
    {
        var status = await engine.StatusAsync();
        int total = status.Applied.Count + status.Pending.Count + status.DestructivePending.Count;

        Banner($"All Migrations  ({total} total)");
        Console.WriteLine($"  {"Version",-20} {"Description",-40} {"Status",-12} {"Applied At"}");
        Console.WriteLine($"  {new string('-', 90)}");

        foreach (var m in status.Applied)
            Console.WriteLine($"  {m.Version,-20} {m.Description,-40} {"Applied",-12} {m.AppliedAt:yyyy-MM-dd HH:mm:ss}");
        foreach (var m in status.Pending)
            Console.WriteLine($"  {m.Version,-20} {m.Description,-40} {"Pending",-12}");
        foreach (var m in status.DestructivePending)
            Console.WriteLine($"  {m.Version,-20} {m.Description,-40} {"DESTRUCTIVE",-12}");

        if (total == 0)
            Console.WriteLine("  No migrations found in the loaded assembly.");

        Ruler();
        return 0;
    }

    // ── history ───────────────────────────────────────────────────────────────

    private static async Task<int> RunHistoryAsync(MigrationEngine engine)
    {
        var status = await engine.StatusAsync();

        Banner("Applied History");
        if (!status.Applied.Any())
        {
            Console.WriteLine("  No migrations have been applied yet.");
        }
        else
        {
            Console.WriteLine($"  {"#",-4} {"Version",-20} {"Description",-40} {"Applied At",-22} {"By"}");
            Console.WriteLine($"  {new string('-', 100)}");
            int i = 1;
            foreach (var m in status.Applied.OrderBy(m => m.Version))
                Console.WriteLine($"  {i++,-4} {m.Version,-20} {m.Description,-40} {m.AppliedAt:yyyy-MM-dd HH:mm:ss}");
        }

        Ruler();
        return 0;
    }

    // ── validate ──────────────────────────────────────────────────────────────

    private static async Task<int> RunValidateAsync(
        MigrationEngine engine, SchemaDriftDetector? detector, Dictionary<string, string> opts)
    {
        Banner("Validation");
        int exitCode = 0;

        if (opts.ContainsKey("check-checksums"))
        {
            Console.WriteLine("  Checking migration checksums...");
            try
            {
                await engine.ValidateChecksumsAsync();
                PrintOk("Checksums valid — no applied migrations have been modified.");
            }
            catch (MigrationTamperedWithException ex)
            {
                PrintError(ex.Message);
                exitCode = 3;
            }
        }

        if (detector != null)
        {
            Console.WriteLine("  Running schema drift detection...");
            try
            {
                var report = await detector.DetectAsync();
                if (report.Issues.Count == 0)
                    PrintOk("No schema drift detected. C# entities match the database.");
                else
                {
                    Console.WriteLine(report.ToString());
                    if (report.HasErrors) exitCode = 1;
                }
            }
            catch (Exception ex)
            {
                PrintError($"Drift detection failed: {ex.Message}");
                exitCode = 1;
            }
        }
        else if (!opts.ContainsKey("check-checksums"))
        {
            Console.WriteLine("  No entity assembly loaded — skipping drift detection.");
            Console.WriteLine("  Use --assembly <path> to enable drift detection.");
        }

        Ruler();
        return exitCode;
    }

    // ── scaffold ──────────────────────────────────────────────────────────────

    private static async Task<int> RunScaffoldAsync(
        ScaffoldGenerator? dbGenerator, EntityMapRegistry? registry,
        CliConfig config, string[] positional, Dictionary<string, string> opts)
    {
        var desc   = positional.FirstOrDefault() ?? opts.GetValueOrDefault("name") ?? "auto_migration";
        var dryRun = opts.ContainsKey("dry-run");
        var outDir = opts.GetValueOrDefault("output") ?? Directory.GetCurrentDirectory();
        var noSnap = opts.ContainsKey("no-snapshot");

        // ── Snapshot mode (default, no live DB needed) ────────────────────────
        if (!noSnap && registry != null)
        {
            var scaffolder = new SnapshotScaffolder(registry);

            if (!dryRun && !SnapshotScaffolder.SnapshotExists(outDir))
                Console.WriteLine($"  No snapshot found — initialising from current model...");
            else if (dryRun)
                Console.WriteLine($"  (dry-run — no files will be written)");
            else
                Console.WriteLine($"  Diffing model against snapshot for: {desc}");

            var result = await scaffolder.ScaffoldAsync(
                desc, outDir, dryRun, config.MigrationsNamespace);
            Console.WriteLine(result);
            return 0;
        }

        // ── Fallback: live DB drift detection ─────────────────────────────────
        if (dbGenerator is null)
            return ErrorExit(
                "Scaffold requires an assembly with [Table] entities.\n" +
                "  Use --assembly <path>   to load your compiled project.\n" +
                "  Use --no-snapshot       to force live-DB drift detection (requires --connection).");

        Console.WriteLine($"  Detecting schema drift against live DB for: {desc}");
        if (dryRun) Console.WriteLine("  (dry-run — no file will be written)");

        var dbResult = await dbGenerator.GenerateAsync(desc, dryRun ? null : outDir, dryRun);
        Console.WriteLine(dbResult);
        return 0;
    }

    // ── new migration ─────────────────────────────────────────────────────────

    private static int CreateBlankMigration(string description, string outputDir, CliConfig config)
    {
        if (!Directory.Exists(outputDir))
        {
            try { Directory.CreateDirectory(outputDir); }
            catch (Exception ex) { return ErrorExit($"Cannot create output directory '{outputDir}': {ex.Message}"); }
        }

        var version = NextVersion(outputDir);
        var className = ToPascalCase(description);
        var ns = config.MigrationsNamespace ?? "Migrations";
        var fileName = Path.Combine(outputDir, $"Migration_{version}_{className}.cs");

        if (File.Exists(fileName))
            return ErrorExit($"File already exists: {fileName}");

        var content = $$"""
            using FluentORM.Core.Attributes;
            using FluentORM.Migrations.Engine;
            using FluentORM.Migrations.Schema;

            namespace {{ns}};

            [Migration({{version}}, "{{description}}")]
            public sealed class {{className}} : Migration
            {
                public override void Up(SchemaBuilder schema)
                {
                    // TODO: implement migration
                    //
                    // Examples:
                    //   schema.CreateTable<MyEntity>(t => { ... });
                    //   schema.AddColumn<MyEntity>(x => x.NewColumn).NotNull().Default("default");
                    //   schema.AddIndex<MyEntity>(x => x.Column);
                    //   schema.Sql("UPDATE ...");
                }

                public override void Down(SchemaBuilder schema)
                {
                    // TODO: implement rollback
                    //
                    // If this migration cannot be reversed, throw:
                    //   throw new IrreversibleMigrationException("Reason why rollback is not possible.");
                }
            }
            """;

        File.WriteAllText(fileName, content);
        PrintOk($"Created {fileName}");
        Console.WriteLine($"  Version  : {version}");
        Console.WriteLine($"  Class    : {className}");
        Console.WriteLine($"  Namespace: {ns}");
        return 0;
    }

    // ── Engine wiring ─────────────────────────────────────────────────────────

    private static EntityMapRegistry BuildRegistry(List<Assembly> assemblies)
    {
        var registry = new EntityMapRegistry();
        foreach (var asm in assemblies)
        {
            try { registry.ScanAssembly(asm); }
            catch { /* non-critical: assembly may have no [Table] types */ }
        }
        return registry;
    }

    private static (MigrationEngine? engine, SchemaDriftDetector? detector, ScaffoldGenerator? generator)
        BuildEngine(CliConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            PrintWarn("No connection string configured.");
            PrintWarn("Provide --connection <string>, set FLUENTORM_CONNECTION, or add connectionString to fluentorm.json.");
            return (null, null, null);
        }

        ISqlDialect dialect;
        IConnectionFactory factory;
        try
        {
            (dialect, factory) = CreateProvider(config);
        }
        catch (Exception ex)
        {
            PrintError($"Failed to create provider: {ex.Message}");
            return (null, null, null);
        }

        var assemblies = LoadAssemblies(config.Assembly);
        var registry   = BuildRegistry(assemblies);

        var engine    = new MigrationEngine(factory, dialect, registry, assemblies);
        var detector  = new SchemaDriftDetector(factory, dialect, registry);
        var generator = new ScaffoldGenerator(detector, registry);

        return (engine, detector, generator);
    }

    private static (ISqlDialect dialect, IConnectionFactory factory) CreateProvider(CliConfig config)
    {
        var cs = config.ConnectionString!;
        return (config.Provider ?? "sqlite").ToLowerInvariant() switch
        {
            "sqlserver" or "mssql" or "sql-server" =>
                ((ISqlDialect)new SqlServerDialect(), (IConnectionFactory)new SqlServerConnectionFactory(cs)),
            "sqlite" =>
                (new SqliteDialect(), new SqliteConnectionFactory(cs)),
            var p =>
                throw new NotSupportedException($"Unknown provider: '{p}'. Valid values: sqlite, sqlserver")
        };
    }

    private static List<Assembly> LoadAssemblies(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return [Assembly.GetEntryAssembly()!];

        var resolved = Path.IsPathRooted(assemblyPath)
            ? assemblyPath
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assemblyPath));

        if (!File.Exists(resolved))
        {
            PrintWarn($"Assembly not found: {resolved}");
            PrintWarn("Migrations from the CLI tool itself will be scanned (likely none).");
            return [Assembly.GetEntryAssembly()!];
        }

        // When the user's DLL has dependencies (Entity Framework providers, domain libraries, etc.)
        // that aren't shipped with this tool, resolve them from the same directory as the user's assembly.
        // FluentORM.* assemblies are already loaded by the tool and will be reused automatically.
        var probeDir = Path.GetDirectoryName(Path.GetFullPath(resolved))!;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var shortName = new AssemblyName(args.Name).Name;
            var candidate = Path.Combine(probeDir, shortName + ".dll");
            if (File.Exists(candidate))
            {
                try { return Assembly.LoadFrom(candidate); }
                catch { /* fall through */ }
            }
            return null;
        };

        // Warn if the user's FluentORM.Migrations version might differ from the tool's.
        WarnOnVersionMismatch(resolved, probeDir);

        try
        {
            return [Assembly.LoadFrom(resolved)];
        }
        catch (Exception ex)
        {
            PrintError($"Could not load assembly '{resolved}': {ex.Message}");
            return [Assembly.GetEntryAssembly()!];
        }
    }

    private static void WarnOnVersionMismatch(string userAssemblyPath, string probeDir)
    {
        var toolMigrationsVersion = typeof(MigrationEngine).Assembly.GetName().Version;
        var userMigrationsPath = Path.Combine(probeDir, "FluentORM.Migrations.dll");
        if (!File.Exists(userMigrationsPath)) return;

        try
        {
            var userVersion = AssemblyName.GetAssemblyName(userMigrationsPath).Version;
            if (userVersion != toolMigrationsVersion)
                PrintWarn(
                    $"Version mismatch: tool ships FluentORM.Migrations {toolMigrationsVersion}, " +
                    $"your project uses {userVersion}. Migration types may be incompatible. " +
                    $"Install FluentORM.Tools {userVersion?.Major}.{userVersion?.Minor}.{userVersion?.Build} to match.");
        }
        catch { /* non-critical */ }
    }

    // ── Config loading ────────────────────────────────────────────────────────

    private static CliConfig LoadConfig(Dictionary<string, string> opts)
    {
        var config = LoadConfigFile(opts.GetValueOrDefault("config"));

        // Environment variables — fill in gaps from file
        config.Provider          ??= Environment.GetEnvironmentVariable("FLUENTORM_PROVIDER");
        config.ConnectionString  ??= Environment.GetEnvironmentVariable("FLUENTORM_CONNECTION");
        config.Assembly          ??= Environment.GetEnvironmentVariable("FLUENTORM_ASSEMBLY");

        // CLI flags — highest priority
        if (opts.TryGetValue("provider",   out var p))  config.Provider = p;
        if (opts.TryGetValue("connection", out var c))  config.ConnectionString = c;
        if (opts.TryGetValue("assembly",   out var a))  config.Assembly = a;
        if (opts.TryGetValue("namespace",  out var ns)) config.MigrationsNamespace = ns;

        config.Provider ??= "sqlite";
        return config;
    }

    private static CliConfig LoadConfigFile(string? explicitPath)
    {
        var path = explicitPath
            ?? Path.Combine(Directory.GetCurrentDirectory(), "fluentorm.json");

        if (!File.Exists(path)) return new CliConfig();

        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<CliConfig>(json,
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? new CliConfig();

            // Resolve relative paths in the config file against the config file's own directory,
            // so a config in /project/fluentorm.json with assembly "./bin/..." works regardless
            // of where the user invokes the tool from.
            if (!string.IsNullOrWhiteSpace(cfg.Assembly) && !Path.IsPathRooted(cfg.Assembly))
            {
                var configDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
                cfg.Assembly = Path.GetFullPath(Path.Combine(configDir, cfg.Assembly));
            }

            return cfg;
        }
        catch (Exception ex)
        {
            PrintWarn($"Could not read config file '{path}': {ex.Message}");
            return new CliConfig();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int HandleMigrationException(Exception ex)
    {
        return ex switch
        {
            DestructiveMigrationException e =>
                ErrorExit($"{e.Message}\n  Re-run with --allow-destructive to proceed.", exitCode: 2),
            MigrationTamperedWithException e =>
                ErrorExit(e.Message, exitCode: 3),
            IrreversibleMigrationException e =>
                ErrorExit(e.Message, exitCode: 4),
            MigrationOrderException e =>
                ErrorExit(e.Message, exitCode: 5),
            MigrationExecutionException e =>
                ErrorExit(e.Message, exitCode: 6),
            _ =>
                ErrorExit($"Unexpected error: {ex.Message}\n{ex.StackTrace}", exitCode: 1)
        };
    }

    private static long NextVersion(string outputDir)
    {
        var prefix = $"{DateTime.UtcNow:yyyyMMdd}";
        var existing = Directory.EnumerateFiles(outputDir, "Migration_*.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(name => { var parts = name.Split('_'); return parts.Length >= 2 ? parts[1] : null; })
            .Where(v => v?.StartsWith(prefix) == true)
            .Select(v => long.TryParse(v, out var n) ? n : 0L)
            .Where(n => n > 0)
            .ToList();

        if (existing.Count == 0) return long.Parse($"{prefix}001");
        var next = existing.Max() + 1;
        // Keep sequence zero-padded to 3 digits: yyyyMMddNNN
        return next;
    }

    private static (string[] positional, Dictionary<string, string> opts) ParseArgs(string[] args)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i][2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    opts[key] = args[++i];
                else
                    opts[key] = "true";
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        return (positional.ToArray(), opts);
    }

    private static string ToPascalCase(string s) =>
        string.Concat(s.Split('_', ' ', '-')
            .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : ""));

    // ── Output ────────────────────────────────────────────────────────────────

    private const string Tick = "✓";
    private const string Dot  = "·";
    private const string Warn = "⚠";

    private static void Banner(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"  {title}");
        Console.WriteLine($"  {new string('─', 62)}");
    }

    private static void Ruler() =>
        Console.WriteLine($"  {new string('─', 62)}");

    private static void PrintOk(string msg)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  {Tick} {msg}");
        Console.ForegroundColor = prev;
    }

    private static void PrintWarn(string msg)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {Warn} {msg}");
        Console.ForegroundColor = prev;
    }

    private static void PrintError(string msg)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"  ERROR: {msg}");
        Console.ForegroundColor = prev;
    }

    private static int ErrorExit(string msg, int exitCode = 1)
    {
        PrintError(msg);
        return exitCode;
    }

    private static int PrintMigrationsHelpAndReturn()
    {
        PrintMigrationsHelp();
        return 0;
    }

    // ── Help text ─────────────────────────────────────────────────────────────

    private static void PrintHelp()
    {
        Console.WriteLine("""
            FluentORM CLI  —  database migration tool

            Usage:
              fluentorm init                          Create a fluentorm.json config file
              fluentorm migrations <command> [flags]  Run migration commands
              fluentorm --help                        Show this help

            Global flags (apply to all migration commands):
              --config <path>        Config file path   (default: ./fluentorm.json)
              --provider <name>      sqlite | sqlserver  (default: sqlite)
              --connection <string>  Database connection string
              --assembly <path>      Path to the .NET assembly containing your migrations
              --namespace <ns>       Namespace for generated migration files

            Environment variables:
              FLUENTORM_PROVIDER     Override provider
              FLUENTORM_CONNECTION   Override connection string
              FLUENTORM_ASSEMBLY     Override assembly path

            Run 'fluentorm migrations --help' for available migration commands.
            """);
    }

    private static void PrintMigrationsHelp()
    {
        Console.WriteLine("""
            FluentORM CLI  —  migration commands

            Usage: fluentorm migrations <command> [flags]

            Informational:
              status                        Show applied, pending, and destructive migrations
              list                          List all migration classes found in the assembly
              history                       Show applied history from the database
              preview                       Print the SQL that would run — no DB changes

            Execution:
              apply                         Apply all safe pending migrations
              apply --allow-destructive     Also apply destructive migrations
              apply --to <version>          Apply up to and including a specific version
              rollback                      Roll back the last applied migration
              rollback --to <version>       Roll back migrations newer than <version>

            Safety:
              validate                      Detect schema drift between C# entities and DB
              validate --check-checksums    Also verify no applied migrations were modified

            Code generation:
              new <description>             Create a blank migration file from a template
              new --output <dir>            Write file to a specific directory (default: CWD)
              scaffold <description>        Auto-generate a migration from model changes (no DB needed)
              scaffold --dry-run            Preview scaffold output without writing any files
              scaffold --output <dir>       Write migration and snapshot to a specific directory
              scaffold --no-snapshot        Fall back to live-DB drift detection (requires --connection)

            Examples:
              fluentorm init
              fluentorm migrations status --connection "Data Source=app.db" --assembly ./App.dll
              fluentorm migrations apply --allow-destructive
              fluentorm migrations rollback --to 20240601003
              fluentorm migrations new add_users_table --output ./src/Migrations
              fluentorm migrations scaffold add_missing_columns --output ./src/Migrations
            """);
    }
}

// ── Config model ──────────────────────────────────────────────────────────────

internal sealed class CliConfig
{
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("connectionString")]
    public string? ConnectionString { get; set; }

    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("migrationsNamespace")]
    public string? MigrationsNamespace { get; set; }
}
