using FluentORM.Core.Abstractions;

namespace FluentORM.SqlServerDemo;

public static class Demo01_BasicCRUD
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 01 — Basic CRUD");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("demo-tenant");

        // ── INSERT ────────────────────────────────────────────────────────────
        Console.WriteLine("\n[INSERT]");

        var farm = new Farm { Name = "Green Valley Farm", Location = "Stellenbosch", HectareSize = 150, TenantId = "demo-tenant" };
        var id = await d.InsertAndGetIdAsync<Farm, int>(farm);
        Console.WriteLine($"  Inserted Farm id={id}");

        // ── FIND BY PK ────────────────────────────────────────────────────────
        Console.WriteLine("\n[FIND BY PK]");

        var found = await d.FindAsync<Farm>(id);
        Console.WriteLine($"  Found: {found!.Name} @ {found.Location} ({found.HectareSize}ha)");

        // ── UPDATE specific columns ───────────────────────────────────────────
        Console.WriteLine("\n[UPDATE specific columns]");

        found.HectareSize = 200;
        await d.UpdateAsync(found, f => new { f.HectareSize });

        var updated = await d.FindAsync<Farm>(id);
        Console.WriteLine($"  HectareSize after update: {updated!.HectareSize}ha");

        // ── UPDATE all columns ────────────────────────────────────────────────
        Console.WriteLine("\n[UPDATE all columns]");

        updated.Location = "Franschhoek";
        await d.UpdateAsync(updated);

        var afterFull = await d.FindAsync<Farm>(id);
        Console.WriteLine($"  Location after full update: {afterFull!.Location}");

        // ── QUERY with WHERE ──────────────────────────────────────────────────
        Console.WriteLine("\n[QUERY with WHERE]");

        await d.InsertAsync(new Farm { Name = "Valley Farm", Location = "Paarl", HectareSize = 80, TenantId = "demo-tenant" });
        await d.InsertAsync(new Farm { Name = "Hilltop Farm", Location = "Franschhoek", HectareSize = 220, TenantId = "demo-tenant" });

        int minSize = 100;
        var largeFarms = await d.Query<Farm>()
            .Where(f => f.HectareSize >= minSize)
            .OrderByDesc(f => f.HectareSize)
            .ToListAsync();

        Console.WriteLine($"  Farms >= {minSize}ha: {largeFarms.Count}");
        foreach (var f in largeFarms)
            Console.WriteLine($"    • {f.Name}: {f.HectareSize}ha");

        // ── COUNT / EXISTS ────────────────────────────────────────────────────
        Console.WriteLine("\n[COUNT / EXISTS]");

        var total = await d.Query<Farm>().CountAsync();
        var hasPaarl = await d.Query<Farm>().Where(f => f.Location == "Paarl").ExistsAsync();
        Console.WriteLine($"  Total farms: {total}");
        Console.WriteLine($"  Has farm in Paarl: {hasPaarl}");

        // ── SOFT DELETE ───────────────────────────────────────────────────────
        Console.WriteLine("\n[SOFT DELETE]");

        await d.DeleteAsync<Farm>(id);
        var afterDelete = await d.Query<Farm>().CountAsync();
        Console.WriteLine($"  Farms after soft-delete: {afterDelete} (deleted is hidden)");

        var withDeleted = await d.Query<Farm>().IncludeDeleted().CountAsync();
        Console.WriteLine($"  Farms including deleted: {withDeleted}");

        // ── RESTORE ───────────────────────────────────────────────────────────
        Console.WriteLine("\n[RESTORE]");

        await d.RestoreAsync<Farm>(id);
        var afterRestore = await d.Query<Farm>().CountAsync();
        Console.WriteLine($"  Farms after restore: {afterRestore}");

        // ── HARD DELETE ───────────────────────────────────────────────────────
        Console.WriteLine("\n[HARD DELETE]");

        await d.HardDeleteAsync<Farm>(id);
        var afterHard = await d.Query<Farm>().IncludeDeleted().CountAsync();
        Console.WriteLine($"  Farms after hard-delete (inc deleted): {afterHard}");
    }
}
