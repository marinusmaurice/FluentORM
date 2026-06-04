using System;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Exceptions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 06 — Optimistic Concurrency
/// [RowVersion] prevents lost-update race conditions.
/// Update and delete require the current version to match.
/// </summary>
public static class Demo06_OptimisticConcurrency
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 06 — Optimistic Concurrency");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("concurrency-demo");

        // ── Insert a versioned product ─────────────────────────────────────────
        Console.WriteLine("\n[Insert versioned Product]");

        var product = new Product
        {
            ExternalId = "CONC-001",
            Name = "Widget",
            Price = 100m,
            Stock = 50,
            Version = 1,
            TenantId = "concurrency-demo"
        };
        var id = await d.InsertAndGetIdAsync<Product, int>(product);
        product.Id = id;
        Console.WriteLine($"  Inserted Product id={id}, Version={product.Version}");

        // ── Load two copies (simulating two users) ────────────────────────────
        Console.WriteLine("\n[Two users load the same product]");

        var userA = await d.FindAsync<Product>(id);
        var userB = await d.FindAsync<Product>(id);
        Console.WriteLine($"  User A loaded: Version={userA!.Version}, Price={userA.Price}");
        Console.WriteLine($"  User B loaded: Version={userB!.Version}, Price={userB.Price}");

        // ── User A updates successfully ────────────────────────────────────────
        Console.WriteLine("\n[User A updates price → success]");

        userA.Price = 120m;
        await d.UpdateAsync(userA, p => new { p.Price });

        var afterA = await d.FindAsync<Product>(id);
        Console.WriteLine($"  After User A: Price={afterA!.Price}, Version={afterA.Version}");

        // ── User B's update is now stale ───────────────────────────────────────
        Console.WriteLine("\n[User B tries to update with stale version → ConcurrencyException]");

        userB.Price = 90m; // User B's change — still version 1, but db is now at version 2
        try
        {
            await d.UpdateAsync(userB, p => new { p.Price });
        }
        catch (ConcurrencyException ex)
        {
            Console.WriteLine($"  ✓ Caught: {ex.Message}");
        }

        // ── Reload and retry pattern ───────────────────────────────────────────
        Console.WriteLine("\n[Reload and retry pattern]");

        var fresh = await d.FindAsync<Product>(id);
        Console.WriteLine($"  Reloaded: Price={fresh!.Price}, Version={fresh.Version}");

        // Apply User B's intended price reduction on fresh data
        fresh.Price = fresh.Price - 30m; // -30 from whatever current is
        await d.UpdateAsync(fresh, p => new { p.Price });

        var final = await d.FindAsync<Product>(id);
        Console.WriteLine($"  After retry: Price={final!.Price}, Version={final.Version}");

        // ── Multiple concurrent updates to demonstrate version increment ───────
        Console.WriteLine("\n[Version increments with each update]");

        for (int i = 0; i < 3; i++)
        {
            var current = await d.FindAsync<Product>(id);
            current!.Stock = current.Stock - 5;
            await d.UpdateAsync(current, p => new { p.Stock });
            Console.WriteLine($"  Update {i + 1}: Stock={current.Stock}, Version={current.Version}");
        }
    }
}
