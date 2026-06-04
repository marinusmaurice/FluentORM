using System;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 01 — Basic CRUD
/// Insert, query, update, delete a Farm entity.
/// </summary>
public static class Demo01_BasicCRUD
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 01 — Basic CRUD");
        Console.WriteLine("═══════════════════════════════════════");

        var tenantDb = db.ForTenant("farm-001");

        // ── INSERT ────────────────────────────────────────────────────────────
        Console.WriteLine("\n[INSERT]");

        var farm = new Farm
        {
            Name = "Sunrise Farm",
            Location = "Stellenbosch",
            HectareSize = 150,
            TenantId = "farm-001"
        };

        var id = await tenantDb.InsertAndGetIdAsync<Farm, int>(farm);
        Console.WriteLine($"  Inserted Farm with Id = {id}");

        // ── FIND BY PK ────────────────────────────────────────────────────────
        Console.WriteLine("\n[FIND BY PK]");

        var found = await tenantDb.FindAsync<Farm>(id);
        Console.WriteLine($"  Found: {found!.Name} at {found.Location} ({found.HectareSize}ha)");

        // ── UPDATE specific columns ───────────────────────────────────────────
        Console.WriteLine("\n[UPDATE specific columns]");

        found.HectareSize = 200;
        await tenantDb.UpdateAsync(found, f => new { f.HectareSize });

        var updated = await tenantDb.FindAsync<Farm>(id);
        Console.WriteLine($"  Updated HectareSize: {updated!.HectareSize}ha");

        // ── UPDATE all columns ────────────────────────────────────────────────
        Console.WriteLine("\n[UPDATE all columns]");

        updated.Location = "Franschhoek";
        await tenantDb.UpdateAsync(updated);

        var afterUpdate = await tenantDb.FindAsync<Farm>(id);
        Console.WriteLine($"  Location changed to: {afterUpdate!.Location}");

        // ── QUERY with WHERE ──────────────────────────────────────────────────
        Console.WriteLine("\n[QUERY with WHERE]");

        await tenantDb.InsertAsync(new Farm { Name = "Valley Farm", Location = "Paarl", HectareSize = 80, TenantId = "farm-001" });
        await tenantDb.InsertAsync(new Farm { Name = "Hilltop Farm", Location = "Franschhoek", HectareSize = 220, TenantId = "farm-001" });

        int minSize = 100;
        var largeFarms = await tenantDb.Query<Farm>()
            .Where(f => f.HectareSize >= minSize)
            .OrderByDesc(f => f.HectareSize)
            .ToListAsync();

        Console.WriteLine($"  Farms >= {minSize}ha: {largeFarms.Count}");
        foreach (var f in largeFarms)
            Console.WriteLine($"    • {f.Name}: {f.HectareSize}ha");

        // ── COUNT / EXISTS ────────────────────────────────────────────────────
        Console.WriteLine("\n[COUNT / EXISTS]");

        var total = await tenantDb.Query<Farm>().CountAsync();
        var hasPaarl = await tenantDb.Query<Farm>().Where(f => f.Location == "Paarl").ExistsAsync();
        Console.WriteLine($"  Total farms: {total}");
        Console.WriteLine($"  Has farm in Paarl: {hasPaarl}");

        // ── DELETE (soft) ─────────────────────────────────────────────────────
        Console.WriteLine("\n[SOFT DELETE]");

        await tenantDb.DeleteAsync<Farm>(id);
        var afterDelete = await tenantDb.Query<Farm>().ToListAsync();
        Console.WriteLine($"  Farms after soft-delete: {afterDelete.Count} (deleted one is hidden)");

        // Shows soft-deleted record is gone from normal queries
        var withDeleted = await tenantDb.Query<Farm>().IncludeDeleted().ToListAsync();
        Console.WriteLine($"  Farms including deleted: {withDeleted.Count}");

        // ── RESTORE ───────────────────────────────────────────────────────────
        Console.WriteLine("\n[RESTORE]");

        await tenantDb.RestoreAsync<Farm>(id);
        var afterRestore = await tenantDb.Query<Farm>().CountAsync();
        Console.WriteLine($"  Farms after restore: {afterRestore}");

        // ── HARD DELETE ───────────────────────────────────────────────────────
        Console.WriteLine("\n[HARD DELETE]");

        await tenantDb.HardDeleteAsync<Farm>(id);
        var afterHard = await tenantDb.Query<Farm>().IncludeDeleted().CountAsync();
        Console.WriteLine($"  Farms after hard delete (inc deleted): {afterHard}");
    }
}
