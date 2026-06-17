using FluentORM.Core.Abstractions;

namespace FluentORM.SqlServerDemo;

public static class Demo02_Queries
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 02 — Queries & Joins");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("query-demo");
        await SeedAsync(d);

        // ── Basic filtering + ordering ────────────────────────────────────────
        Console.WriteLine("\n[WHERE + ORDER BY]");

        var largeFarms = await d.Query<Farm>()
            .Where(f => f.HectareSize > 100)
            .OrderByDesc(f => f.HectareSize)
            .ToListAsync();

        Console.WriteLine($"  Farms > 100ha: {largeFarms.Count}");
        foreach (var f in largeFarms)
            Console.WriteLine($"    • {f.Name}: {f.HectareSize}ha");

        // ── Projection (SELECT specific columns) ──────────────────────────────
        Console.WriteLine("\n[SELECT projection]");

        var names = await d.Query<Farm>()
            .Select(f => new { f.Name, f.Location })
            .OrderBy(f => f.Name)
            .ToListAsync();

        foreach (var n in names)
            Console.WriteLine($"  {n}");

        // ── LIKE / string contains ────────────────────────────────────────────
        Console.WriteLine("\n[WHERE string contains]");

        var franschhoekFarms = await d.Query<Farm>()
            .Where(f => f.Location.Contains("schhoek"))
            .ToListAsync();

        Console.WriteLine($"  Farms in 'schhoek': {franschhoekFarms.Count}");

        // ── WhereIn ───────────────────────────────────────────────────────────
        Console.WriteLine("\n[WhereIn]");

        var locations = new[] { "Stellenbosch", "Paarl" };
        var inFarms = await d.Query<Farm>()
            .WhereIn(f => f.Location, locations)
            .ToListAsync();

        Console.WriteLine($"  Farms in Stellenbosch/Paarl: {inFarms.Count}");

        // ── Aggregates ────────────────────────────────────────────────────────
        Console.WriteLine("\n[Aggregates]");

        var totalFarms = await d.Query<Farm>().CountAsync();
        var avgHa = await d.Query<Farm>().AverageAsync(f => f.HectareSize);
        var maxHa = await d.Query<Farm>().MaxAsync(f => f.HectareSize);

        Console.WriteLine($"  Count: {totalFarms}, Avg ha: {avgHa:F1}, Max ha: {maxHa}");

        // ── JOIN SQL preview ──────────────────────────────────────────────────
        Console.WriteLine("\n[JOIN — Farm to Fields SQL]");

        var joinSql = d.Query<Farm>()
            .Join<Field>((farm, field) => farm.Id == field.FarmId)
            .ToSql();

        Console.WriteLine(joinSql);

        // ── Keyset pagination ─────────────────────────────────────────────────
        Console.WriteLine("\n[Keyset pagination]");

        var page1 = await d.Query<Farm>()
            .OrderBy(f => f.Id)
            .Take(2)
            .ToListAsync();

        Console.WriteLine($"  Page 1: {string.Join(", ", page1.ConvertAll(f => f.Name))}");

        if (page1.Count > 0)
        {
            var lastId = page1[^1].Id;
            var page2 = await d.Query<Farm>()
                .Where(f => f.Id > lastId)
                .OrderBy(f => f.Id)
                .Take(2)
                .ToListAsync();

            Console.WriteLine($"  Page 2: {string.Join(", ", page2.ConvertAll(f => f.Name))}");
        }

        // ── Offset pagination ─────────────────────────────────────────────────
        Console.WriteLine("\n[Offset pagination (OFFSET … FETCH NEXT — T-SQL)]");

        var offsetPage = await d.Query<Farm>()
            .OrderBy(f => f.Name)
            .Skip(1)
            .Take(2)
            .ToListAsync();

        Console.WriteLine($"  Offset page (skip 1, take 2): {string.Join(", ", offsetPage.ConvertAll(f => f.Name))}");

        // ── DISTINCT ──────────────────────────────────────────────────────────
        Console.WriteLine("\n[DISTINCT locations]");

        var distinctSql = d.Query<Farm>()
            .Select(f => new { f.Location })
            .Distinct()
            .OrderBy(f => f.Location)
            .ToSql();

        Console.WriteLine(distinctSql);

        // ── Raw SQL (T-SQL specific) ───────────────────────────────────────────
        Console.WriteLine("\n[Raw T-SQL — TOP 3 largest farms]");

        var top3 = await d.RawAsync<Farm>(
            "SELECT TOP 3 * FROM Farms WHERE TenantId = @tid AND DeletedAt IS NULL ORDER BY HectareSize DESC",
            new { tid = "query-demo" });

        foreach (var f in top3)
            Console.WriteLine($"  {f.Name}: {f.HectareSize}ha");
    }

    private static async Task SeedAsync(IFluentDb d)
    {
        var farms = new[]
        {
            new Farm { Name = "Sunrise Farm",  Location = "Stellenbosch", HectareSize = 150, TenantId = "query-demo" },
            new Farm { Name = "Valley Farm",   Location = "Paarl",        HectareSize = 80,  TenantId = "query-demo" },
            new Farm { Name = "Hilltop Farm",  Location = "Franschhoek",  HectareSize = 220, TenantId = "query-demo" },
            new Farm { Name = "River Farm",    Location = "Stellenbosch", HectareSize = 310, TenantId = "query-demo" },
            new Farm { Name = "Mountain Farm", Location = "Franschhoek",  HectareSize = 95,  TenantId = "query-demo" },
        };
        await d.BulkInsertAsync(farms);
    }
}
