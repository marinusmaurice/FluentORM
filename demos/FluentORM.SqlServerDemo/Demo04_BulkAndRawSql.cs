using FluentORM.Core.Abstractions;

namespace FluentORM.SqlServerDemo;

public static class Demo04_BulkAndRawSql
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 04 — Bulk Operations & Raw T-SQL");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("bulk-demo");

        // ── Bulk insert ───────────────────────────────────────────────────────
        Console.WriteLine("\n[BulkInsertAsync]");

        var farms = new List<Farm>();
        for (int i = 1; i <= 10; i++)
            farms.Add(new Farm { Name = $"Farm {i:D2}", Location = i % 2 == 0 ? "Stellenbosch" : "Paarl", HectareSize = i * 20, TenantId = "bulk-demo" });

        await d.BulkInsertAsync(farms);
        Console.WriteLine($"  Inserted {farms.Count} farms");

        // ── Bulk update where ─────────────────────────────────────────────────
        Console.WriteLine("\n[BulkUpdateAsync — mark small farms inactive]");

        var updated = await d.BulkUpdateAsync<Farm>(
            where: f => f.HectareSize < 100,
            columns: f => new { f.Active },
            values: new { Active = false });

        Console.WriteLine($"  Deactivated {updated} small farms (< 100ha)");

        // ── Bulk delete where ─────────────────────────────────────────────────
        Console.WriteLine("\n[BulkDeleteAsync — soft delete Paarl farms]");

        var deleted = await d.BulkDeleteAsync<Farm>(f => f.Location == "Paarl");
        Console.WriteLine($"  Soft-deleted {deleted} Paarl farms");

        var remaining = await d.Query<Farm>().CountAsync();
        Console.WriteLine($"  Remaining (non-deleted): {remaining}");

        // ── Upsert ────────────────────────────────────────────────────────────
        Console.WriteLine("\n[UpsertAsync — insert or update on ExternalId]");

        var productA = new Product { ExternalId = "SKU-001", Name = "Fertiliser A", Price = 299.99m, Stock = 100, TenantId = "bulk-demo" };
        await d.UpsertAsync(productA, conflictOn: p => new { p.ExternalId });
        Console.WriteLine("  Upserted SKU-001 (first time → insert)");

        productA.Price = 349.99m;
        productA.Stock = 150;
        await d.UpsertAsync(productA, conflictOn: p => new { p.ExternalId });
        Console.WriteLine("  Upserted SKU-001 (second time → update)");

        var fetched = await d.Query<Product>().Where(p => p.ExternalId == "SKU-001").FirstOrDefaultAsync();
        Console.WriteLine($"  SKU-001 price after upsert: R{fetched!.Price:N2}, stock: {fetched.Stock}");

        // ── Raw T-SQL ─────────────────────────────────────────────────────────
        Console.WriteLine("\n[RawAsync — T-SQL with TOP and GETUTCDATE()]");

        var top5 = await d.RawAsync<Farm>(
            "SELECT TOP 5 * FROM Farms WHERE TenantId = @tid AND DeletedAt IS NULL ORDER BY HectareSize DESC",
            new { tid = "bulk-demo" });

        Console.WriteLine("  Top 5 largest active farms:");
        foreach (var f in top5)
            Console.WriteLine($"    • {f.Name}: {f.HectareSize}ha");

        // ── ScalarAsync — server-side aggregate via raw T-SQL ─────────────────
        Console.WriteLine("\n[ScalarAsync — SUM via raw T-SQL]");

        var totalHa = await d.ScalarAsync<int>(
            "SELECT ISNULL(SUM(HectareSize), 0) FROM Farms WHERE TenantId = @tid AND DeletedAt IS NULL",
            new { tid = "bulk-demo" });

        Console.WriteLine($"  Total active hectares: {totalHa}ha");

        // ── ExecuteAsync — DDL-style raw command ──────────────────────────────
        Console.WriteLine("\n[ExecuteAsync — raw UPDATE statement]");

        var rows = await d.ExecuteAsync(
            "UPDATE Farms SET Location = 'Western Cape' WHERE TenantId = @tid AND Location = 'Stellenbosch'",
            new { tid = "bulk-demo" });

        Console.WriteLine($"  Rows updated: {rows}");
    }
}
