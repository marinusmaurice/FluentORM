using System;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 09 — Heavy SQL: stacks joins, subqueries, GROUP BY/HAVING, EXISTS,
/// WhereIn-subquery, DISTINCT and multi-column sort/paging into single
/// queries to show the most complicated SQL FluentORM can generate.
/// </summary>
public static class Demo09_HeavySql
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 09 — Heavy / Complicated SQL");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("heavy-demo");
        await SeedAsync(d);

        // ── 1. 3-table join + GROUP BY + HAVING + ORDER BY + paging ───────────
        Console.WriteLine("\n[1] 3-table JOIN + GROUP BY + HAVING + ORDER BY + LIMIT/OFFSET");

        var heavySql = d.Query<Inspection>()
            .Join<Field>((i, f) => i.FieldId == f.Id)
            .Join<Field, Farm>((i, f, farm) => f.FarmId == farm.Id)
            .Where(i => i.SeverityScore > 1.0)
            .WhereNotNull(i => i.Notes)
            .GroupBy(i => i.FieldId)
            .Having(i => i.SeverityScore > 3.0)
            .OrderByDesc(i => i.SeverityScore)
            .ThenBy(i => i.FieldId)
            .Skip(0)
            .Take(5)
            .ToSql();

        Console.WriteLine(heavySql);

        // ── 2. WhereIn subquery — fields belonging to active large farms ──────
        Console.WriteLine("\n[2] WhereIn subquery — inspections in fields of active, large farms");

        var bigActiveFarmFieldIds = d.Query<Farm>()
            .Where(f => f.Active)
            .Where(f => f.HectareSize > 150);

        var subquerySql = d.Query<Field>()
            .WhereIn(f => f.FarmId, bigActiveFarmFieldIds)
            .ToSql();

        Console.WriteLine(subquerySql);

        // ── 3. WhereExists combined with WhereNotIn + WhereBetween + OrWhere ──
        Console.WriteLine("\n[3] WhereExists + WhereNotIn + WhereBetween + OrWhere — kitchen-sink filter");

        var since = DateTime.UtcNow.AddDays(-30);
        var until = DateTime.UtcNow;

        var kitchenSinkSql = d.Query<Farm>()
            .WhereExists<Field>((farm, field) => farm.Id == field.FarmId)
            .WhereNotIn(f => f.Location, new[] { "Nowhere", "Unknown" })
            .WhereBetween(f => f.HectareSize, 50, 600)
            .OrWhere(f => f.Name == "Sunrise Estate")
            .OrderBy(f => f.Location)
            .ThenByDesc(f => f.HectareSize)
            .ToSql();

        Console.WriteLine(kitchenSinkSql);

        // ── 4. DISTINCT projection across a join, sorted ───────────────────────
        Console.WriteLine("\n[4] DISTINCT crop types across joined fields, sorted");

        var distinctSql = d.Query<Field>()
            .Join<Farm>((field, farm) => field.FarmId == farm.Id)
            .Select(f => new { f.CropType })
            .Distinct()
            .OrderBy(f => f.CropType)
            .ToSql();

        Console.WriteLine(distinctSql);

        // ── 5. Raw CTE — multi-level aggregation hand-written via RawAsync ─────
        Console.WriteLine("\n[5] Raw CTE — farms whose worst field beats the tenant average severity");

        var worstFieldFarms = await d.RawAsync<WorstFieldFarmDto>(
            @"WITH field_avg AS (
                  SELECT f.Id AS FieldId, f.FarmId, AVG(i.SeverityScore) AS AvgSeverity
                  FROM Fields f
                  JOIN Inspections i ON i.FieldId = f.Id
                  WHERE f.TenantId = @tid
                  GROUP BY f.Id, f.FarmId
              ),
              farm_worst AS (
                  SELECT FarmId, MAX(AvgSeverity) AS WorstFieldAvg
                  FROM field_avg
                  GROUP BY FarmId
              ),
              tenant_avg AS (
                  SELECT AVG(SeverityScore) AS Overall FROM Inspections WHERE TenantId = @tid
              )
              SELECT fm.Name AS FarmName, fw.WorstFieldAvg AS WorstFieldAvg
              FROM farm_worst fw
              JOIN Farms fm ON fm.Id = fw.FarmId
              CROSS JOIN tenant_avg ta
              WHERE fw.WorstFieldAvg > ta.Overall
              ORDER BY fw.WorstFieldAvg DESC",
            new { tid = "heavy-demo" });

        Console.WriteLine("-- Generated SQL (printed above) executed against SQLite:");
        foreach (var r in worstFieldFarms)
            Console.WriteLine($"  {r.FarmName,-15} worst field avg severity = {r.WorstFieldAvg:F2}");

        Console.WriteLine("\n  [Demo 09 complete]");
    }

    private class WorstFieldFarmDto
    {
        public string FarmName { get; set; } = string.Empty;
        public double WorstFieldAvg { get; set; }
    }

    private static async Task SeedAsync(IFluentDb d)
    {
        var f1 = await d.InsertAndGetIdAsync<Farm, int>(new Farm { Name = "Sunrise Estate", Location = "Stellenbosch", HectareSize = 320, Active = true, TenantId = "heavy-demo" });
        var f2 = await d.InsertAndGetIdAsync<Farm, int>(new Farm { Name = "River Bend", Location = "Cape Town", HectareSize = 180, Active = true, TenantId = "heavy-demo" });
        var f3 = await d.InsertAndGetIdAsync<Farm, int>(new Farm { Name = "Dune Farm", Location = "Paarl", HectareSize = 90, Active = false, TenantId = "heavy-demo" });

        var fld1 = await d.InsertAndGetIdAsync<Field, int>(new Field { Name = "North Block", FarmId = f1, AreaHectares = 100, CropType = "Wheat", TenantId = "heavy-demo" });
        var fld2 = await d.InsertAndGetIdAsync<Field, int>(new Field { Name = "South Block", FarmId = f1, AreaHectares = 80, CropType = "Barley", TenantId = "heavy-demo" });
        var fld3 = await d.InsertAndGetIdAsync<Field, int>(new Field { Name = "East Paddock", FarmId = f2, AreaHectares = 60, CropType = "Maize", TenantId = "heavy-demo" });
        var fld4 = await d.InsertAndGetIdAsync<Field, int>(new Field { Name = "West Paddock", FarmId = f3, AreaHectares = 200, CropType = "Canola", TenantId = "heavy-demo" });

        var pest = await d.InsertAndGetIdAsync<Pest, int>(new Pest { Name = "Bollworm", RiskLevel = 5, Category = "Larvae", TenantId = "heavy-demo" });

        var now = DateTime.UtcNow;
        await d.BulkInsertAsync(new[]
        {
            new Inspection { FieldId = fld1, PestId = pest, SeverityScore = 4.5, Notes = "Heavy", InspectedAt = now.AddDays(-5), TenantId = "heavy-demo" },
            new Inspection { FieldId = fld1, PestId = pest, SeverityScore = 2.0, Notes = "Mild", InspectedAt = now.AddDays(-3), TenantId = "heavy-demo" },
            new Inspection { FieldId = fld2, PestId = pest, SeverityScore = 5.5, Notes = "Critical", InspectedAt = now.AddDays(-2), TenantId = "heavy-demo" },
            new Inspection { FieldId = fld3, PestId = pest, SeverityScore = 1.2, Notes = "Low", InspectedAt = now.AddDays(-1), TenantId = "heavy-demo" },
            new Inspection { FieldId = fld4, PestId = pest, SeverityScore = 6.8, Notes = "Severe", InspectedAt = now, TenantId = "heavy-demo" },
        });
    }
}
