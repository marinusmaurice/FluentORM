using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 02 — Advanced Query API
/// WHERE variants, joins, grouping, paging, projections, subqueries, diagnostics.
/// </summary>
public static class Demo02_Queries
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 02 — Advanced Queries");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("query-demo");
        await SeedDataAsync(d);

        // ── ToSql() — see the generated SQL without executing ─────────────────
        Console.WriteLine("\n[ToSql() — Preview generated SQL]");

        var sql = d.Query<Farm>()
            .Where(f => f.HectareSize > 100)
            .OrderByDesc(f => f.HectareSize)
            .Skip(0).Take(5)
            .ToSql();
        Console.WriteLine(sql);

        // ── WHERE variants ────────────────────────────────────────────────────
        Console.WriteLine("\n[WHERE variants]");

        // Contains → LIKE '%..%'
        var searchTerm = "South";
        var nameLike = await d.Query<Farm>()
            .Where(f => f.Name.Contains(searchTerm))
            .ToListAsync();
        Console.WriteLine($"  Contains '{searchTerm}': {nameLike.Count} farm(s)");

        // StartsWith
        var startsWith = await d.Query<Farm>()
            .Where(f => f.Name.StartsWith("River"))
            .ToListAsync();
        Console.WriteLine($"  StartsWith 'River': {startsWith.Count} farm(s)");

        // WhereBetween
        var midSized = await d.Query<Farm>()
            .WhereBetween(f => f.HectareSize, 50, 150)
            .ToListAsync();
        Console.WriteLine($"  Between 50-150ha: {midSized.Count} farm(s)");

        // WhereIn
        var targets = new[] { "Stellenbosch", "Paarl" };
        var inList = await d.Query<Farm>()
            .WhereIn(f => f.Location, targets)
            .ToListAsync();
        Console.WriteLine($"  WhereIn [Stellenbosch,Paarl]: {inList.Count} farm(s)");

        // WhereNotIn
        var notIn = await d.Query<Farm>()
            .WhereNotIn(f => f.Location, targets)
            .ToListAsync();
        Console.WriteLine($"  WhereNotIn [Stellenbosch,Paarl]: {notIn.Count} farm(s)");

        // WhereNull / WhereNotNull
        var noDelete = await d.Query<Farm>()
            .WhereNull(f => f.DeletedAt)
            .CountAsync();
        Console.WriteLine($"  WhereNull(DeletedAt): {noDelete} farm(s)");

        // OrWhere
        var either = await d.Query<Farm>()
            .Where(f => f.HectareSize > 200)
            .OrWhere(f => f.Location == "Paarl")
            .ToListAsync();
        Console.WriteLine($"  >200ha OR in Paarl: {either.Count} farm(s)");

        // WhereRaw
        var raw = await d.Query<Farm>()
            .WhereRaw("length(Name) > {0}", 10)
            .ToListAsync();
        Console.WriteLine($"  WhereRaw name.length > 10: {raw.Count} farm(s)");

        // ── JOIN ──────────────────────────────────────────────────────────────
        Console.WriteLine("\n[JOIN — Farm + Field]");

        var joinSql = d.Query<Farm>()
            .Join<Field>((farm, field) => farm.Id == field.FarmId)
            .ToSql();
        Console.WriteLine(joinSql);

        // LEFT JOIN
        var leftJoinSql = d.Query<Farm>()
            .LeftJoin<Field>((farm, field) => farm.Id == field.FarmId)
            .ToSql();
        Console.WriteLine("\n[LEFT JOIN SQL]");
        Console.WriteLine(leftJoinSql);

        // ── GROUP BY + HAVING ─────────────────────────────────────────────────
        Console.WriteLine("\n[GROUP BY + HAVING]");

        var groupSql = d.Query<Inspection>()
            .GroupBy(i => i.FieldId)
            .Having(i => i.SeverityScore > 3.0)
            .OrderByDesc(i => i.SeverityScore)
            .ToSql();
        Console.WriteLine(groupSql);

        // ── PAGING ────────────────────────────────────────────────────────────
        Console.WriteLine("\n[PAGING]");

        var page = await d.Query<Farm>()
            .OrderBy(f => f.Name)
            .ToPagedAsync(page: 0, pageSize: 2);

        Console.WriteLine($"  Page 0 (size 2): {page.Items.Count} items, {page.TotalCount} total, {page.TotalPages} pages");
        foreach (var f in page.Items)
            Console.WriteLine($"    • {f.Name}");

        // ── DISTINCT ──────────────────────────────────────────────────────────
        Console.WriteLine("\n[DISTINCT]");

        var distinctSql = d.Query<Farm>()
            .Select(f => new { f.Location })
            .Distinct()
            .ToSql();
        Console.WriteLine(distinctSql);

        // ── PROJECTIONS ───────────────────────────────────────────────────────
        Console.WriteLine("\n[SELECT projection]");

        var projSql = d.Query<Farm>()
            .Select(f => new { f.Id, f.Name, f.HectareSize })
            .Where(f => f.Active == true)
            .ToSql();
        Console.WriteLine(projSql);

        // ── DIAGNOSTICS ───────────────────────────────────────────────────────
        Console.WriteLine("\n[WithDiagnostics]");

        var (results, diag) = await d.Query<Farm>()
            .Where(f => f.HectareSize > 50)
            .WithDiagnostics()
            .ToListWithDiagnosticsAsync();

        Console.WriteLine($"  Rows returned: {results.Count}");
        Console.WriteLine($"  Execution: {diag.ExecutionMs:F1}ms");
        Console.WriteLine($"  SQL:\n{diag.Sql}");

        // ── RAW SQL ───────────────────────────────────────────────────────────
        Console.WriteLine("\n[Raw SQL escape hatch]");

        var countRaw = await d.ScalarAsync<int>(
            "SELECT COUNT(*) FROM Farms WHERE TenantId = @t",
            new { t = "query-demo" });
        Console.WriteLine($"  Raw COUNT: {countRaw}");
    }

    private static async Task SeedDataAsync(IFluentDb d)
    {
        var farms = new[]
        {
            new Farm { Name = "Riverside Farm",   Location = "Stellenbosch", HectareSize = 120, TenantId = "query-demo" },
            new Farm { Name = "South Valley",      Location = "Paarl",        HectareSize = 75,  TenantId = "query-demo" },
            new Farm { Name = "Hilltop Estate",    Location = "Franschhoek",  HectareSize = 250, TenantId = "query-demo" },
            new Farm { Name = "River Bend Farm",   Location = "Stellenbosch", HectareSize = 50,  TenantId = "query-demo" },
            new Farm { Name = "Mountain View",     Location = "Wellington",    HectareSize = 180, TenantId = "query-demo" },
        };
        foreach (var f in farms)
            await d.InsertAsync(f);
    }
}
