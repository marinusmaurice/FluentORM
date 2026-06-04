using System;
using System.Data;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Exceptions;

namespace FluentORM.Demos;

/// <summary>
/// Demo 05 — Transactions and Savepoints
/// Auto-commit, auto-rollback, savepoints for partial rollback.
/// </summary>
public static class Demo05_Transactions
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 05 — Transactions & Savepoints");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("txn-demo");

        // ── Auto-commit on success ────────────────────────────────────────────
        Console.WriteLine("\n[TransactionAsync — auto-commit on success]");

        await d.TransactionAsync(async tx =>
        {
            var farmId = await tx.InsertAndGetIdAsync<Farm, int>(
                new Farm { Name = "TX Farm A", Location = "Cape Town", HectareSize = 100, TenantId = "txn-demo" });

            await tx.InsertAsync(
                new Field { Name = "North Field", FarmId = farmId, AreaHectares = 40, CropType = "Wheat", TenantId = "txn-demo" });

            await tx.InsertAsync(
                new Field { Name = "South Field", FarmId = farmId, AreaHectares = 60, CropType = "Maize", TenantId = "txn-demo" });

            Console.WriteLine($"  Inserted farm {farmId} with 2 fields (will commit)");
        });

        var farms = await d.Query<Farm>().CountAsync();
        var fields = await d.Query<Field>().CountAsync();
        Console.WriteLine($"  After commit: {farms} farm(s), {fields} field(s)");

        // ── Auto-rollback on exception ────────────────────────────────────────
        Console.WriteLine("\n[TransactionAsync — auto-rollback on exception]");

        try
        {
            await d.TransactionAsync(async tx =>
            {
                await tx.InsertAsync(
                    new Farm { Name = "TX Farm B", Location = "Durban", HectareSize = 50, TenantId = "txn-demo" });

                Console.WriteLine("  Inserted farm B (about to throw)...");
                throw new InvalidOperationException("Simulated failure mid-transaction");
            });
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"  ✓ Caught: {ex.Message}");
        }

        var farmsAfterRollback = await d.Query<Farm>().CountAsync();
        Console.WriteLine($"  Farms after rollback: {farmsAfterRollback} (farm B was NOT committed)");

        // ── Savepoints for partial rollback ───────────────────────────────────
        Console.WriteLine("\n[Savepoints — partial rollback within transaction]");

        await using var txWithSavepoint = await d.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            // Main operation (will succeed)
            var farmId = await txWithSavepoint.InsertAndGetIdAsync<Farm, int>(
                new Farm { Name = "TX Farm C", Location = "Pretoria", HectareSize = 75, TenantId = "txn-demo" });
            Console.WriteLine($"  Inserted Farm C (id={farmId}) — will keep");

            await txWithSavepoint.SavepointAsync("before_fields");

            try
            {
                // Sub-operation that might fail
                await txWithSavepoint.InsertAsync(
                    new Field { Name = "Bad Field", FarmId = farmId, AreaHectares = 0, CropType = "None", TenantId = "txn-demo" });
                // FK violation would normally throw here
                // For demo purposes we'll simulate a logic failure:
                throw new InvalidOperationException("Field data was invalid");
            }
            catch (InvalidOperationException ex)
            {
                await txWithSavepoint.RollbackToAsync("before_fields");
                Console.WriteLine($"  ✓ Field insert failed: {ex.Message}");
                Console.WriteLine("  ✓ Rolled back to savepoint — Farm C still alive");
            }

            await txWithSavepoint.CommitAsync();
        }
        catch
        {
            await txWithSavepoint.RollbackAsync();
            throw;
        }

        var farmC = await d.Query<Farm>().Where(f => f.Name == "TX Farm C").FirstOrDefaultAsync();
        Console.WriteLine($"  Farm C exists after savepoint rollback: {farmC != null}");

        var fieldsForC = await d.Query<Field>().CountAsync();
        Console.WriteLine($"  Fields after savepoint rollback: {fieldsForC} (bad field gone)");

        // ── Manual transaction control ────────────────────────────────────────
        Console.WriteLine("\n[Manual transaction control]");

        await using var txn = await d.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await txn.InsertAsync(
                new Farm { Name = "Manual TX Farm", Location = "East London", HectareSize = 30, TenantId = "txn-demo" });
            await txn.CommitAsync();
            Console.WriteLine("  Manual transaction committed");
        }
        catch
        {
            await txn.RollbackAsync();
            throw;
        }

        var total = await d.Query<Farm>().CountAsync();
        Console.WriteLine($"  Total farms: {total}");
    }
}
