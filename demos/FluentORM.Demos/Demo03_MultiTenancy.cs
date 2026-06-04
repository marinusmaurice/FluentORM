using System;
using System.Linq;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Exceptions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 03 — Structural Multi-Tenancy
/// TenantId auto-injection, ForTenant scoping, cross-tenant protection.
/// </summary>
public static class Demo03_MultiTenancy
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 03 — Multi-Tenancy");
        Console.WriteLine("═══════════════════════════════════════");

        var tenant1 = db.ForTenant("acme-corp");
        var tenant2 = db.ForTenant("globex-ltd");
        var tenant3 = db.ForTenant("initech");

        // ── Seed data for each tenant ─────────────────────────────────────────
        Console.WriteLine("\n[Seeding data for 3 tenants]");

        await tenant1.InsertAsync(new Farm { Name = "ACME Farm A", Location = "Cape Town", HectareSize = 100, TenantId = "acme-corp" });
        await tenant1.InsertAsync(new Farm { Name = "ACME Farm B", Location = "Durban",    HectareSize = 80,  TenantId = "acme-corp" });
        await tenant2.InsertAsync(new Farm { Name = "Globex North", Location = "Johannesburg", HectareSize = 200, TenantId = "globex-ltd" });
        await tenant2.InsertAsync(new Farm { Name = "Globex South", Location = "Cape Town",    HectareSize = 150, TenantId = "globex-ltd" });
        await tenant3.InsertAsync(new Farm { Name = "Initech HQ Farm", Location = "Pretoria",  HectareSize = 60, TenantId = "initech" });

        Console.WriteLine("  3 tenants, 5 farms total seeded.");

        // ── Each tenant only sees their own data ──────────────────────────────
        Console.WriteLine("\n[Tenant isolation — each sees only their own data]");

        var acmeFarms    = await tenant1.Query<Farm>().ToListAsync();
        var globexFarms  = await tenant2.Query<Farm>().ToListAsync();
        var initechFarms = await tenant3.Query<Farm>().ToListAsync();

        Console.WriteLine($"  ACME sees:    {acmeFarms.Count} farm(s): {string.Join(", ", acmeFarms.Select(f => f.Name))}");
        Console.WriteLine($"  Globex sees:  {globexFarms.Count} farm(s): {string.Join(", ", globexFarms.Select(f => f.Name))}");
        Console.WriteLine($"  Initech sees: {initechFarms.Count} farm(s): {string.Join(", ", initechFarms.Select(f => f.Name))}");

        // ── Generated SQL shows tenant filter ─────────────────────────────────
        Console.WriteLine("\n[Generated SQL — tenant filter is always injected]");
        var sql = tenant1.Query<Farm>().Where(f => f.HectareSize > 50).ToSql();
        Console.WriteLine(sql);

        // ── TenantMismatchException ───────────────────────────────────────────
        Console.WriteLine("\n[TenantMismatchException — can't write wrong-tenant entity]");

        try
        {
            // Trying to insert a ACME entity while logged in as Globex
            var wrongEntity = new Farm { Name = "Smuggled Farm", TenantId = "acme-corp", HectareSize = 0 };
            await tenant2.InsertAsync(wrongEntity); // tenant2 = globex-ltd, entity says acme-corp
        }
        catch (TenantMismatchException ex)
        {
            Console.WriteLine($"  ✓ Caught TenantMismatchException: {ex.Message}");
        }

        // ── QueryAllTenants (admin) ───────────────────────────────────────────
        Console.WriteLine("\n[QueryAllTenants — cross-tenant admin view]");

        var allFarms = await db.QueryAllTenants<Farm>().OrderBy(f => f.TenantId).ToListAsync();
        Console.WriteLine($"  All tenants combined: {allFarms.Count} farm(s)");
        foreach (var g in allFarms.GroupBy(f => f.TenantId))
            Console.WriteLine($"    [{g.Key}]: {string.Join(", ", g.Select(f => f.Name))}");

        // ── Scoped tenant context for background jobs ─────────────────────────
        Console.WriteLine("\n[ForTenant() scoped context — simulating a background job]");

        var jobDb = db.ForTenant("acme-corp");
        var jobFarms = await jobDb.Query<Farm>().Where(f => f.Active == true).ToListAsync();
        Console.WriteLine($"  Background job for ACME sees {jobFarms.Count} active farm(s)");
    }
}
