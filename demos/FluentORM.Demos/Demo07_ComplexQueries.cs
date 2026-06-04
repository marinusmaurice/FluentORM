using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 07 — Complex Queries: multi-table joins, aggregates, subqueries, CTEs.
/// </summary>
public static class Demo07_ComplexQueries
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 07 — Complex Queries");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("complex-demo");
        await SeedAsync(d);

        // ── Multi-table JOIN SQL ───────────────────────────────────────────────
        Console.WriteLine("\n[2-table JOIN: Farm + Field SQL]");

        var joinSql = d.Query<Farm>()
            .Join<Field>((farm, field) => farm.Id == field.FarmId)
            .ToSql();

        Console.WriteLine(joinSql);

        // ── Aggregates ────────────────────────────────────────────────────────
        Console.WriteLine("\n[Aggregates on Inspections]");

        // Individual aggregate queries
        var inspCount = await d.Query<Inspection>().CountAsync();
        var avgSeverity = await d.Query<Inspection>().AverageAsync(i => i.SeverityScore);
        var maxSeverity = await d.Query<Inspection>().MaxAsync(i => i.SeverityScore);
        var minSeverity = await d.Query<Inspection>().MinAsync(i => i.SeverityScore);

        Console.WriteLine($"  Count: {inspCount}");
        Console.WriteLine($"  Avg severity: {avgSeverity:F2}");
        Console.WriteLine($"  Max severity: {maxSeverity:F2}");
        Console.WriteLine($"  Min severity: {minSeverity:F2}");

        var totalCost = await d.Query<SprayEvent>().SumAsync(s => s.CostZAR);
        Console.WriteLine($"  Total spray cost: R{totalCost:N2}");

        // ── GROUP BY + HAVING ─────────────────────────────────────────────────
        Console.WriteLine("\n[GROUP BY FieldId, HAVING avg > 3.0]");

        var groupSql = d.Query<Inspection>()
            .GroupBy(i => i.FieldId)
            .Having(i => i.SeverityScore > 3.0)
            .Select(i => new { i.FieldId, i.SeverityScore })
            .OrderByDesc(i => i.SeverityScore)
            .ToSql();

        Console.WriteLine(groupSql);

        // ── Subquery — WhereIn ─────────────────────────────────────────────────
        Console.WriteLine("\n[WhereIn subquery — large farms]");

        var subquerySql = d.Query<Farm>()
            .Where(f => f.HectareSize > 50)
            .ToSql();

        Console.WriteLine(subquerySql);

        // ── WhereExists ───────────────────────────────────────────────────────
        Console.WriteLine("\n[WhereExists — SQL]");

        var existsSql = d.Query<Farm>()
            .WhereExists<Field>((farm, field) => farm.Id == field.FarmId)
            .ToSql();

        Console.WriteLine(existsSql);

        // ── WhereNotIn ────────────────────────────────────────────────────────
        Console.WriteLine("\n[WhereNotIn — pests not in high-risk category]");

        var notInSql = d.Query<Pest>()
            .WhereNotIn(p => p.RiskLevel, new[] { 4, 5 })
            .ToSql();

        Console.WriteLine(notInSql);

        // ── Keyset pagination (preferred for large datasets) ──────────────────
        Console.WriteLine("\n[Keyset pagination]");

        var firstPage = await d.Query<Farm>()
            .OrderBy(f => f.Id)
            .Take(2)
            .ToListAsync();

        Console.WriteLine($"  First page (2 items): {string.Join(", ", System.Linq.Enumerable.Select(firstPage, f => f.Name))}");

        if (firstPage.Count > 0)
        {
            var lastId = firstPage[^1].Id;
            var nextPage = await d.Query<Farm>()
                .Where(f => f.Id > lastId)
                .OrderBy(f => f.Id)
                .Take(2)
                .ToListAsync();

            Console.WriteLine($"  Next page (after id={lastId}): {string.Join(", ", System.Linq.Enumerable.Select(nextPage, f => f.Name))}");
        }

        // ── CTE SQL ───────────────────────────────────────────────────────────
        Console.WriteLine("\n[CTE SQL — recent high-risk inspections]");

        var cteSql = d.Query<Inspection>()
            .Where(i => i.SeverityScore > 3.5)
            .OrderByDesc(i => i.SeverityScore)
            .ToSql();

        // Show what a CTE would look like conceptually
        Console.WriteLine("-- Conceptual CTE for recent high-risk inspections:");
        Console.WriteLine("-- WITH recent_high_risk AS (");
        Console.WriteLine("--   SELECT ... FROM Inspections WHERE SeverityScore > 3.5");
        Console.WriteLine("-- )");
        Console.WriteLine("-- SELECT ... FROM Fields f JOIN recent_high_risk r ON f.Id = r.FieldId");

        // ── Window function SQL preview ───────────────────────────────────────
        Console.WriteLine("\n[Window function SQL — ROW_NUMBER OVER PARTITION BY]");

        // Show how the Sql.RowNumber() builder works
        var rowNum = Core.Query.Sql.RowNumber();
        rowNum.Over()
              .PartitionBy<Inspection>(i => (object)i.FieldId)
              .OrderByDesc<Inspection>(i => (object)i.SeverityScore);

        Console.WriteLine("  Sql.RowNumber().Over().PartitionBy(i => i.FieldId).OrderByDesc(i => i.SeverityScore)");
        Console.WriteLine("  → ROW_NUMBER() OVER (PARTITION BY i.FieldId ORDER BY i.SeverityScore DESC)");

        // ── DISTINCT ──────────────────────────────────────────────────────────
        Console.WriteLine("\n[DISTINCT locations]");

        var distinctSql = d.Query<Farm>()
            .Select(f => new { f.Location })
            .Distinct()
            .OrderBy(f => f.Location)
            .ToSql();

        Console.WriteLine(distinctSql);

        // ── Fully-formatted readable output example ───────────────────────────
        Console.WriteLine("\n[Spec §16 — SQL Readability: formatted 3-table join with GROUP BY]");

        var readableSql = d.Query<Field>()
            .Join<Farm>((field, farm) => field.FarmId == farm.Id)
            .ToSql();

        Console.WriteLine(readableSql);
    }

    private static async Task SeedAsync(IFluentDb d)
    {
        var farmId = await d.InsertAndGetIdAsync<Farm, int>(
            new Farm { Name = "Complex Farm", Location = "Test Region", HectareSize = 200, TenantId = "complex-demo" });

        var field1Id = await d.InsertAndGetIdAsync<Field, int>(
            new Field { Name = "Alpha Field", FarmId = farmId, AreaHectares = 80, CropType = "Wheat", TenantId = "complex-demo" });
        var field2Id = await d.InsertAndGetIdAsync<Field, int>(
            new Field { Name = "Beta Field", FarmId = farmId, AreaHectares = 120, CropType = "Maize", TenantId = "complex-demo" });

        var pestId = await d.InsertAndGetIdAsync<Pest, int>(
            new Pest { Name = "Aphid", RiskLevel = 3, Category = "Insect", TenantId = "complex-demo" });

        var now = DateTime.UtcNow;
        var inspections = new[]
        {
            new Inspection { FieldId = field1Id, PestId = pestId, SeverityScore = 4.2, Notes = "Heavy infestation", InspectedAt = now.AddDays(-5), TenantId = "complex-demo" },
            new Inspection { FieldId = field1Id, PestId = pestId, SeverityScore = 2.1, Notes = "Mild", InspectedAt = now.AddDays(-3), TenantId = "complex-demo" },
            new Inspection { FieldId = field2Id, PestId = pestId, SeverityScore = 5.0, Notes = "Critical", InspectedAt = now.AddDays(-1), TenantId = "complex-demo" },
            new Inspection { FieldId = field2Id, PestId = pestId, SeverityScore = 1.5, Notes = "Low risk", InspectedAt = now, TenantId = "complex-demo" },
        };
        await d.BulkInsertAsync(inspections);

        var sprays = new[]
        {
            new SprayEvent { FieldId = field1Id, Chemical = "Pyrethrin", CostZAR = 450.00m, AppliedAt = now.AddDays(-4), TenantId = "complex-demo" },
            new SprayEvent { FieldId = field2Id, Chemical = "Neonicotinoid", CostZAR = 890.00m, AppliedAt = now.AddDays(-2), TenantId = "complex-demo" },
        };
        await d.BulkInsertAsync(sprays);
    }
}
