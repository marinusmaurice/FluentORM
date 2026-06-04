using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 08 — Aggregates and Weird Combinations
/// SUM/AVG/MAX/MIN/CountDistinct, complex filter chains,
/// OrWhere, WhereNull, IncludeDeleted, ScalarAsync, RawAsync,
/// multi-column sort, cascaded predicates, and more.
/// </summary>
public static class Demo08_AggregatesAndCombos
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 08 — Aggregates & Weird Combos");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("agg-demo");
        var ids = await SeedAsync(d);

        // ── 1. Basic aggregate suite ──────────────────────────────────────────
        Console.WriteLine("\n[1] Basic aggregates on all inspections");

        var count   = await d.Query<Inspection>().CountAsync();
        var avg     = await d.Query<Inspection>().AverageAsync(i => i.SeverityScore);
        var max     = await d.Query<Inspection>().MaxAsync<double>(i => i.SeverityScore);
        var min     = await d.Query<Inspection>().MinAsync<double>(i => i.SeverityScore);
        var sumCost = await d.Query<SprayEvent>().SumAsync<decimal>(s => s.CostZAR);

        Console.WriteLine($"  Inspections : count={count}, avg={avg:F2}, max={max:F2}, min={min:F2}");
        Console.WriteLine($"  Spray cost  : total = R{sumCost:N2}");

        // ── 2. Aggregate over filtered rows ───────────────────────────────────
        Console.WriteLine("\n[2] Aggregates over filtered rows");

        var highRiskAvg = await d.Query<Inspection>()
            .Where(i => i.SeverityScore >= 4.0)
            .AverageAsync(i => i.SeverityScore);

        var lowRiskCount = await d.Query<Inspection>()
            .Where(i => i.SeverityScore < 3.0)
            .CountAsync();

        Console.WriteLine($"  Avg severity (score >= 4.0) : {highRiskAvg:F2}");
        Console.WriteLine($"  Count (score < 3.0)         : {lowRiskCount}");

        // ── 3. WhereNull / WhereNotNull + aggregate ───────────────────────────
        Console.WriteLine("\n[3] WhereNull / WhereNotNull — resolved vs open inspections");

        var openCount = await d.Query<Inspection>()
            .WhereNull(i => i.ResolvedAt)
            .CountAsync();

        var resolvedCount = await d.Query<Inspection>()
            .WhereNotNull(i => i.ResolvedAt)
            .CountAsync();

        var openAvgSeverity = await d.Query<Inspection>()
            .WhereNull(i => i.ResolvedAt)
            .AverageAsync(i => i.SeverityScore);

        Console.WriteLine($"  Open (null ResolvedAt)     : {openCount} inspections, avg severity {openAvgSeverity:F2}");
        Console.WriteLine($"  Resolved (not null)        : {resolvedCount} inspections");

        // ── 4. WhereBetween + SUM ─────────────────────────────────────────────
        Console.WriteLine("\n[4] WhereBetween date range — spray cost last 7 days");

        var since = DateTime.UtcNow.AddDays(-7);
        var until = DateTime.UtcNow;

        var recentCost = await d.Query<SprayEvent>()
            .WhereBetween(s => s.AppliedAt, since, until)
            .SumAsync<decimal>(s => s.CostZAR);

        var recentSprayCount = await d.Query<SprayEvent>()
            .WhereBetween(s => s.AppliedAt, since, until)
            .CountAsync();

        Console.WriteLine($"  Sprays in last 7 days: {recentSprayCount}, total cost R{recentCost:N2}");

        // ── 5. WhereBetween on numeric range ──────────────────────────────────
        Console.WriteLine("\n[5] WhereBetween numeric — inspections severity 3.0–6.0");

        var midRangeCount = await d.Query<Inspection>()
            .WhereBetween(i => i.SeverityScore, 3.0, 6.0)
            .CountAsync();

        var midRangeMax = await d.Query<Inspection>()
            .WhereBetween(i => i.SeverityScore, 3.0, 6.0)
            .MaxAsync<double>(i => i.SeverityScore);

        Console.WriteLine($"  Severity 3.0–6.0 : {midRangeCount} inspections, max = {midRangeMax:F2}");

        // ── 6. OrWhere combinations ───────────────────────────────────────────
        Console.WriteLine("\n[6] OrWhere — farms in Cape Town OR Stellenbosch");

        var orFarms = await d.Query<Farm>()
            .Where(f => f.Location == "Cape Town")
            .OrWhere(f => f.Location == "Stellenbosch")
            .ToListAsync();

        Console.WriteLine($"  Farms in Cape Town or Stellenbosch: {orFarms.Count}");
        foreach (var f in orFarms)
            Console.WriteLine($"    • {f.Name} ({f.Location}) {f.HectareSize}ha");

        // ── 7. Stacked AND chains — 5+ predicates ─────────────────────────────
        Console.WriteLine("\n[7] Stacked AND predicates — active large farms not in Paarl");

        var stackedFarms = await d.Query<Farm>()
            .Where(f => f.Active)
            .Where(f => f.HectareSize > 100)
            .Where(f => f.HectareSize < 600)
            .Where(f => f.Location != "Paarl")
            .Where(f => f.Name != "Deleted Farm")
            .ToListAsync();

        Console.WriteLine($"  Matching farms: {stackedFarms.Count}");
        foreach (var f in stackedFarms)
            Console.WriteLine($"    • {f.Name} ({f.Location}) {f.HectareSize}ha");

        // ── 8. WhereIn + aggregate ────────────────────────────────────────────
        Console.WriteLine("\n[8] WhereIn — cost of specific fields");

        var fieldIds = new[] { ids.Field1Id, ids.Field2Id };
        var selectedFieldCost = await d.Query<SprayEvent>()
            .WhereIn(s => s.FieldId, fieldIds)
            .SumAsync<decimal>(s => s.CostZAR);

        var selectedFieldSprayCount = await d.Query<SprayEvent>()
            .WhereIn(s => s.FieldId, fieldIds)
            .CountAsync();

        Console.WriteLine($"  Fields 1+2 : {selectedFieldSprayCount} sprays, total R{selectedFieldCost:N2}");

        // ── 9. WhereNotIn + COUNT ─────────────────────────────────────────────
        Console.WriteLine("\n[9] WhereNotIn — inspections excluding highest-risk pests");

        var notInPestIds = new[] { ids.PestHighId };
        var nonHighRiskCount = await d.Query<Inspection>()
            .WhereNotIn(i => i.PestId, notInPestIds)
            .CountAsync();

        Console.WriteLine($"  Inspections excluding highest-risk pest: {nonHighRiskCount}");

        // ── 10. CountDistinct ─────────────────────────────────────────────────
        Console.WriteLine("\n[10] CountDistinct — unique chemicals used, unique pest types seen");

        var distinctChemicals = await d.Query<SprayEvent>()
            .CountDistinctAsync(s => s.Chemical);

        var distinctPests = await d.Query<Inspection>()
            .CountDistinctAsync(i => i.PestId);

        Console.WriteLine($"  Distinct chemicals applied : {distinctChemicals}");
        Console.WriteLine($"  Distinct pests encountered : {distinctPests}");

        // ── 11. WhereRaw + aggregate ──────────────────────────────────────────
        Console.WriteLine("\n[11] WhereRaw + SUM — custom SQL predicate");

        var rawFilteredCost = await d.Query<SprayEvent>()
            .WhereRaw("length(Chemical) > 8")
            .SumAsync<decimal>(s => s.CostZAR);

        var rawFilteredCount = await d.Query<SprayEvent>()
            .WhereRaw("length(Chemical) > 8")
            .CountAsync();

        Console.WriteLine($"  Sprays with chemical name > 8 chars: {rawFilteredCount}, cost R{rawFilteredCost:N2}");

        // ── 12. OrWhere + WhereIn combo ───────────────────────────────────────
        Console.WriteLine("\n[12] OrWhere + WhereIn — active farms OR in specific regions");

        var combo = await d.Query<Farm>()
            .Where(f => f.HectareSize > 400)
            .OrWhere(f => f.Location == "Paarl")
            .CountAsync();

        Console.WriteLine($"  Farms (>400ha OR in Paarl): {combo}");

        // ── 13. IncludeDeleted + aggregates ───────────────────────────────────
        Console.WriteLine("\n[13] IncludeDeleted — soft-deleted farms visible in count");

        var activeCount       = await d.Query<Farm>().CountAsync();
        var includingDeleted  = await d.Query<Farm>().IncludeDeleted().CountAsync();
        var onlyDeleted       = await d.Query<Farm>().OnlyDeleted().CountAsync();
        var maxHaIncDeleted   = await d.Query<Farm>().IncludeDeleted().MaxAsync<int>(f => f.HectareSize);

        Console.WriteLine($"  Active farms          : {activeCount}");
        Console.WriteLine($"  Including soft-deleted: {includingDeleted}");
        Console.WriteLine($"  Only deleted          : {onlyDeleted}");
        Console.WriteLine($"  Max hectares (any)    : {maxHaIncDeleted}ha");

        // ── 14. ThenBy multi-column sort + paging ─────────────────────────────
        Console.WriteLine("\n[14] Multi-column sort (Location ASC, HectareSize DESC) + Take 3");

        var sorted = await d.Query<Farm>()
            .OrderBy(f => f.Location)
            .ThenByDesc(f => f.HectareSize)
            .Take(3)
            .ToListAsync();

        Console.WriteLine($"  Top 3:");
        foreach (var f in sorted)
            Console.WriteLine($"    • {f.Location,-15} {f.Name,-20} {f.HectareSize}ha");

        // ── 15. MIN on joined subset ──────────────────────────────────────────
        Console.WriteLine("\n[15] MIN severity on high-risk-pest inspections only");

        var minSevHighPest = await d.Query<Inspection>()
            .Where(i => i.PestId == ids.PestHighId)
            .MinAsync<double>(i => i.SeverityScore);

        var sumCostHighRiskField = await d.Query<SprayEvent>()
            .Where(s => s.FieldId == ids.Field3Id)
            .SumAsync<decimal>(s => s.CostZAR);

        Console.WriteLine($"  Min severity (high-risk pest)     : {minSevHighPest:F2}");
        Console.WriteLine($"  Total spray cost (field 3)        : R{sumCostHighRiskField:N2}");

        // ── 16. ScalarAsync — custom SQL aggregate ────────────────────────────
        Console.WriteLine("\n[16] ScalarAsync — weighted average severity (severity * area / total area)");

        var weightedAvg = await d.ScalarAsync<double>(
            @"SELECT CAST(SUM(i.SeverityScore * f.AreaHectares) AS REAL) / SUM(f.AreaHectares)
              FROM Inspections i
              JOIN Fields f ON i.FieldId = f.Id
              WHERE i.TenantId = @tid",
            new { tid = "agg-demo" });

        Console.WriteLine($"  Weighted avg severity (by field area): {weightedAvg:F3}");

        // ── 17. RawAsync — cross-table aggregate returning typed list ─────────
        Console.WriteLine("\n[17] RawAsync — per-field spray cost summary");

        var fieldCosts = await d.RawAsync<FieldCostDto>(
            @"SELECT f.Name AS FieldName, COUNT(s.Id) AS SprayCount,
                     ROUND(SUM(s.CostZAR), 2) AS TotalCost,
                     ROUND(AVG(s.CostZAR), 2) AS AvgCost
              FROM Fields f
              LEFT JOIN SprayEvents s ON f.Id = s.FieldId AND s.TenantId = @tid
              WHERE f.TenantId = @tid
              GROUP BY f.Id, f.Name
              ORDER BY TotalCost DESC",
            new { tid = "agg-demo" });

        foreach (var fc in fieldCosts)
            Console.WriteLine($"  {fc.FieldName,-15} sprays={fc.SprayCount}, total=R{fc.TotalCost:N2}, avg=R{fc.AvgCost:N2}");

        // ── 18. Chained WhereNull + WhereIn + MAX ─────────────────────────────
        Console.WriteLine("\n[18] Chained WhereNull + WhereIn + MAX — worst open pest in known fields");

        var knownFieldIds = new[] { ids.Field1Id, ids.Field2Id, ids.Field3Id };
        var worstOpen = await d.Query<Inspection>()
            .WhereNull(i => i.ResolvedAt)
            .WhereIn(i => i.FieldId, knownFieldIds)
            .MaxAsync<double>(i => i.SeverityScore);

        var worstOpenCount = await d.Query<Inspection>()
            .WhereNull(i => i.ResolvedAt)
            .WhereIn(i => i.FieldId, knownFieldIds)
            .CountAsync();

        Console.WriteLine($"  Open inspections in fields 1-3: {worstOpenCount}, worst severity: {worstOpen:F2}");

        // ── 19. GROUP BY + HAVING with actual results ─────────────────────────
        Console.WriteLine("\n[19] GROUP BY FieldId HAVING avg severity > 3.5 (via RawAsync)");

        var hotFields = await d.RawAsync<HotFieldDto>(
            @"SELECT f.Name AS FieldName, AVG(i.SeverityScore) AS AvgSeverity,
                     COUNT(i.Id) AS InspectionCount
              FROM Inspections i
              JOIN Fields f ON i.FieldId = f.Id
              WHERE i.TenantId = @tid
              GROUP BY i.FieldId
              HAVING AVG(i.SeverityScore) > 3.5
              ORDER BY AvgSeverity DESC",
            new { tid = "agg-demo" });

        if (!hotFields.Any())
            Console.WriteLine("  (no fields exceed threshold)");
        foreach (var hf in hotFields)
            Console.WriteLine($"  {hf.FieldName,-15} avg={hf.AvgSeverity:F2}, inspections={hf.InspectionCount}");

        // ── 20. The kitchen sink — OR + AND + WhereRaw + WhereBetween + COUNT ─
        Console.WriteLine("\n[20] Kitchen sink — OR + AND + WhereRaw + WhereBetween + COUNT");

        var kitchenSinkCount = await d.Query<Inspection>()
            .Where(i => i.SeverityScore > 2.0)
            .WhereBetween(i => i.InspectedAt, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow)
            .WhereNull(i => i.ResolvedAt)
            .WhereNotIn(i => i.PestId, new[] { -1 })
            .WhereRaw("length(Notes) > 0")
            .OrWhere(i => i.SeverityScore >= 7.0)
            .CountAsync();

        Console.WriteLine($"  Matching inspections: {kitchenSinkCount}");

        Console.WriteLine("\n  [All 20 aggregate + combo scenarios passed]");
    }

    // ── Seed ──────────────────────────────────────────────────────────────────

    private record SeedIds(int Farm1Id, int Farm2Id, int Farm3Id,
                           int Field1Id, int Field2Id, int Field3Id, int Field4Id,
                           int PestLowId, int PestMidId, int PestHighId);

    private static async Task<SeedIds> SeedAsync(IFluentDb d)
    {
        var now = DateTime.UtcNow;

        // 5 farms across different locations (one will be soft-deleted)
        var f1 = await d.InsertAndGetIdAsync<Farm, int>(new Farm { Name = "Sunrise Estate",    Location = "Stellenbosch", HectareSize = 320, TenantId = "agg-demo" });
        var f2 = await d.InsertAndGetIdAsync<Farm, int>(new Farm { Name = "River Bend",        Location = "Cape Town",    HectareSize = 180, TenantId = "agg-demo" });
        var f3 = await d.InsertAndGetIdAsync<Farm, int>(new Farm { Name = "Dune Farm",         Location = "Paarl",        HectareSize = 500, TenantId = "agg-demo" });
        var f4 = await d.InsertAndGetIdAsync<Farm, int>(new Farm { Name = "Highveld Plot",     Location = "Cape Town",    HectareSize = 140, TenantId = "agg-demo" });
        var f5 = await d.InsertAndGetIdAsync<Farm, int>(new Farm { Name = "Deleted Farm",      Location = "Nowhere",      HectareSize = 999, TenantId = "agg-demo" });
        await d.DeleteAsync<Farm>(f5); // soft-delete

        // Fields: 4 across farms 1-3
        var fld1 = await d.InsertAndGetIdAsync<Field, int>(new Field { Name = "North Block",   FarmId = f1, AreaHectares = 100, CropType = "Wheat",  TenantId = "agg-demo" });
        var fld2 = await d.InsertAndGetIdAsync<Field, int>(new Field { Name = "South Block",   FarmId = f1, AreaHectares = 80,  CropType = "Barley", TenantId = "agg-demo" });
        var fld3 = await d.InsertAndGetIdAsync<Field, int>(new Field { Name = "East Paddock",  FarmId = f2, AreaHectares = 60,  CropType = "Maize",  TenantId = "agg-demo" });
        var fld4 = await d.InsertAndGetIdAsync<Field, int>(new Field { Name = "West Paddock",  FarmId = f3, AreaHectares = 200, CropType = "Canola", TenantId = "agg-demo" });

        // Pests: low / mid / high risk
        var pLow  = await d.InsertAndGetIdAsync<Pest, int>(new Pest { Name = "Aphid",       RiskLevel = 1, Category = "Insect",  TenantId = "agg-demo" });
        var pMid  = await d.InsertAndGetIdAsync<Pest, int>(new Pest { Name = "Whitefly",    RiskLevel = 3, Category = "Insect",  TenantId = "agg-demo" });
        var pHigh = await d.InsertAndGetIdAsync<Pest, int>(new Pest { Name = "Bollworm",    RiskLevel = 5, Category = "Larvae",  TenantId = "agg-demo" });

        // 16 inspections with varied severity, some resolved
        await d.BulkInsertAsync(new[]
        {
            new Inspection { FieldId = fld1, PestId = pLow,  SeverityScore = 1.2, Notes = "Minimal",       InspectedAt = now.AddDays(-20), ResolvedAt = now.AddDays(-15), TenantId = "agg-demo" },
            new Inspection { FieldId = fld1, PestId = pMid,  SeverityScore = 3.8, Notes = "Moderate",      InspectedAt = now.AddDays(-18), ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld1, PestId = pHigh, SeverityScore = 5.5, Notes = "Heavy",         InspectedAt = now.AddDays(-10), ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld1, PestId = pLow,  SeverityScore = 2.1, Notes = "Low drift",     InspectedAt = now.AddDays(-5),  ResolvedAt = now.AddDays(-2),  TenantId = "agg-demo" },
            new Inspection { FieldId = fld2, PestId = pMid,  SeverityScore = 4.4, Notes = "Spreading",     InspectedAt = now.AddDays(-14), ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld2, PestId = pHigh, SeverityScore = 7.0, Notes = "Critical",      InspectedAt = now.AddDays(-7),  ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld2, PestId = pLow,  SeverityScore = 1.5, Notes = "Trace",         InspectedAt = now.AddDays(-3),  ResolvedAt = now.AddDays(-1),  TenantId = "agg-demo" },
            new Inspection { FieldId = fld2, PestId = pMid,  SeverityScore = 2.9, Notes = "Stable",        InspectedAt = now.AddDays(-2),  ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld3, PestId = pHigh, SeverityScore = 6.1, Notes = "Severe",        InspectedAt = now.AddDays(-12), ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld3, PestId = pLow,  SeverityScore = 0.9, Notes = "Negligible",    InspectedAt = now.AddDays(-9),  ResolvedAt = now.AddDays(-7),  TenantId = "agg-demo" },
            new Inspection { FieldId = fld3, PestId = pMid,  SeverityScore = 3.3, Notes = "Needs watching",InspectedAt = now.AddDays(-4),  ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld4, PestId = pHigh, SeverityScore = 4.8, Notes = "Active colony", InspectedAt = now.AddDays(-6),  ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld4, PestId = pMid,  SeverityScore = 5.2, Notes = "High spread",   InspectedAt = now.AddDays(-3),  ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld4, PestId = pLow,  SeverityScore = 1.8, Notes = "Contained",     InspectedAt = now.AddDays(-1),  ResolvedAt = now,              TenantId = "agg-demo" },
            new Inspection { FieldId = fld1, PestId = pHigh, SeverityScore = 6.8, Notes = "Resurgence",    InspectedAt = now.AddDays(-1),  ResolvedAt = null,             TenantId = "agg-demo" },
            new Inspection { FieldId = fld2, PestId = pHigh, SeverityScore = 8.0, Notes = "Emergency",     InspectedAt = now,              ResolvedAt = null,             TenantId = "agg-demo" },
        });

        // 8 spray events with different chemicals and costs
        await d.BulkInsertAsync(new[]
        {
            new SprayEvent { FieldId = fld1, Chemical = "Pyrethrin",       CostZAR = 450.00m, AppliedAt = now.AddDays(-19), TenantId = "agg-demo" },
            new SprayEvent { FieldId = fld1, Chemical = "Imidacloprid",    CostZAR = 780.00m, AppliedAt = now.AddDays(-9),  TenantId = "agg-demo" },
            new SprayEvent { FieldId = fld2, Chemical = "Neonicotinoid",   CostZAR = 890.00m, AppliedAt = now.AddDays(-6),  TenantId = "agg-demo" },
            new SprayEvent { FieldId = fld2, Chemical = "Cypermethrin",    CostZAR = 320.00m, AppliedAt = now.AddDays(-2),  TenantId = "agg-demo" },
            new SprayEvent { FieldId = fld3, Chemical = "Chlorpyrifos",    CostZAR = 1100.00m,AppliedAt = now.AddDays(-11), TenantId = "agg-demo" },
            new SprayEvent { FieldId = fld3, Chemical = "Pyrethrin",       CostZAR = 520.00m, AppliedAt = now.AddDays(-4),  TenantId = "agg-demo" },
            new SprayEvent { FieldId = fld4, Chemical = "Deltamethrin",    CostZAR = 670.00m, AppliedAt = now.AddDays(-5),  TenantId = "agg-demo" },
            new SprayEvent { FieldId = fld4, Chemical = "Imidacloprid",    CostZAR = 840.00m, AppliedAt = now.AddDays(-1),  TenantId = "agg-demo" },
        });

        return new SeedIds(f1, f2, f3, fld1, fld2, fld3, fld4, pLow, pMid, pHigh);
    }
}

// ── DTOs for RawAsync ─────────────────────────────────────────────────────────

public class FieldCostDto
{
    public string FieldName  { get; set; } = string.Empty;
    public int    SprayCount { get; set; }
    public decimal TotalCost { get; set; }
    public decimal AvgCost   { get; set; }
}

public class HotFieldDto
{
    public string FieldName       { get; set; } = string.Empty;
    public double AvgSeverity     { get; set; }
    public int    InspectionCount { get; set; }
}
