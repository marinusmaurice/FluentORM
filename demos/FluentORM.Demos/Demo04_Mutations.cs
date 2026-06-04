using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 04 — Explicit Mutation API
/// InsertOrIgnore, Upsert, UpdateWhere, BulkInsert, BulkUpsert.
/// Every mutation names its columns — nothing is tracked silently.
/// </summary>
public static class Demo04_Mutations
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 04 — Explicit Mutations");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("mutation-demo");

        // ── InsertOrIgnore ────────────────────────────────────────────────────
        Console.WriteLine("\n[InsertOrIgnore — idempotent on conflict]");

        var p1 = new Product { ExternalId = "SKU-001", Name = "Herbicide A", Price = 299.99m, Stock = 50, TenantId = "mutation-demo" };
        var p2 = new Product { ExternalId = "SKU-002", Name = "Fertilizer B", Price = 149.50m, Stock = 100, TenantId = "mutation-demo" };

        await d.InsertAsync(p1);
        await d.InsertAsync(p2);

        // Try inserting the same ExternalId again — should silently ignore
        var duplicate = new Product { ExternalId = "SKU-001", Name = "DIFFERENT NAME", Price = 999m, Stock = 0, TenantId = "mutation-demo" };
        await d.InsertOrIgnoreAsync(duplicate, p => new { p.ExternalId });

        var afterIgnore = await d.Query<Product>().Where(p => p.ExternalId == "SKU-001").FirstAsync();
        Console.WriteLine($"  SKU-001 name is still: '{afterIgnore.Name}' (duplicate was ignored)");

        // ── Upsert ────────────────────────────────────────────────────────────
        Console.WriteLine("\n[Upsert — insert or update on conflict]");

        var upsertProduct = new Product
        {
            ExternalId = "SKU-001",
            Name = "Herbicide A v2",   // updated
            Price = 349.99m,            // updated
            Stock = 45,
            TenantId = "mutation-demo"
        };

        await d.UpsertAsync(upsertProduct,
            conflictOn: p => new { p.ExternalId },
            updateOnly: p => new { p.Name, p.Price });

        var afterUpsert = await d.Query<Product>().Where(p => p.ExternalId == "SKU-001").FirstAsync();
        Console.WriteLine($"  After upsert: name='{afterUpsert.Name}', price={afterUpsert.Price:C}");

        // New product via upsert
        var newViaUpsert = new Product { ExternalId = "SKU-003", Name = "Pesticide C", Price = 199m, Stock = 75, TenantId = "mutation-demo" };
        await d.UpsertAsync(newViaUpsert, p => new { p.ExternalId });

        var count = await d.Query<Product>().CountAsync();
        Console.WriteLine($"  Products after upsert-insert: {count}");

        // ── UpdateWhere (no entity load required) ─────────────────────────────
        Console.WriteLine("\n[UpdateWhere — bulk update by predicate]");

        var rowsUpdated = await d.UpdateWhereAsync<Product>(
            where: p => p.Stock < 60,
            columns: p => new { p.Stock },
            values: new { Stock = 60 });

        Console.WriteLine($"  UpdateWhere (stock < 60 → 60): {rowsUpdated} rows updated");

        var lowStock = await d.Query<Product>().Where(p => p.Stock < 60).CountAsync();
        Console.WriteLine($"  Products still below 60 stock: {lowStock}");

        // ── BulkInsert ────────────────────────────────────────────────────────
        Console.WriteLine("\n[BulkInsert — many rows in one operation]");

        var batch = new List<Farm>();
        for (int i = 1; i <= 10; i++)
            batch.Add(new Farm { Name = $"Batch Farm {i:D2}", Location = "Bulk Region", HectareSize = i * 10, TenantId = "mutation-demo" });

        await d.BulkInsertAsync(batch, f => new { f.Name, f.Location, f.HectareSize, f.TenantId, f.Active });

        var farmsCount = await d.Query<Farm>().CountAsync();
        Console.WriteLine($"  Farms after bulk insert: {farmsCount}");

        // ── BulkUpdate ────────────────────────────────────────────────────────
        Console.WriteLine("\n[BulkUpdate — activate all bulk-inserted farms]");

        var bulkUpdated = await d.BulkUpdateAsync<Farm>(
            where: f => f.Location == "Bulk Region",
            columns: f => new { f.Active },
            values: new { Active = false });

        Console.WriteLine($"  Deactivated {bulkUpdated} bulk farms");

        // ── DeleteWhere ───────────────────────────────────────────────────────
        Console.WriteLine("\n[DeleteWhere — remove by predicate]");

        var deleted = await d.DeleteWhereAsync<Farm>(f => f.Location == "Bulk Region");
        Console.WriteLine($"  Soft-deleted {deleted} bulk farms");

        var remaining = await d.Query<Farm>().CountAsync();
        Console.WriteLine($"  Remaining active farms: {remaining}");

        // ── BulkUpsert ────────────────────────────────────────────────────────
        Console.WriteLine("\n[BulkUpsert — sync a product catalog]");

        var catalog = new[]
        {
            new Product { ExternalId = "SKU-001", Name = "Herbicide A Pro", Price = 399m, Stock = 50, TenantId = "mutation-demo" },
            new Product { ExternalId = "SKU-004", Name = "Fungicide D",      Price = 250m, Stock = 30, TenantId = "mutation-demo" },
            new Product { ExternalId = "SKU-005", Name = "Growth Booster",   Price = 175m, Stock = 80, TenantId = "mutation-demo" },
        };

        await d.BulkUpsertAsync(catalog, p => new { p.ExternalId }, p => new { p.Name, p.Price, p.Stock });

        var finalCount = await d.Query<Product>().CountAsync();
        Console.WriteLine($"  Products after bulk upsert: {finalCount}");
    }
}
