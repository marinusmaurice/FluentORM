using System.Data;
using FluentORM.Core.Abstractions;

namespace FluentORM.SqlServerDemo;

public static class Demo03_Transactions
{
    public static async Task RunAsync(IFluentDb db)
    {
        Console.WriteLine("\n═══════════════════════════════════════");
        Console.WriteLine(" Demo 03 — Transactions & Savepoints");
        Console.WriteLine("═══════════════════════════════════════");

        var d = db.ForTenant("txn-demo");

        // ── Auto-commit on success ────────────────────────────────────────────
        Console.WriteLine("\n[TransactionAsync — auto-commit]");

        await d.TransactionAsync(async tx =>
        {
            var farmId = await tx.InsertAndGetIdAsync<Farm, int>(
                new Farm { Name = "TX Farm A", Location = "Cape Town", HectareSize = 100, TenantId = "txn-demo" });

            await tx.InsertAsync(
                new Field { Name = "North Field", FarmId = farmId, AreaHectares = 40, CropType = "Wheat", TenantId = "txn-demo" });

            Console.WriteLine($"  Inserted farm id={farmId} with 1 field");
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
                throw new InvalidOperationException("Simulated failure");
            });
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"  Caught expected exception: {ex.Message}");
        }

        var farmsAfterRollback = await d.Query<Farm>().CountAsync();
        Console.WriteLine($"  Farms after rollback: {farmsAfterRollback} (Farm B was NOT committed)");

        // ── Savepoints for partial rollback ───────────────────────────────────
        Console.WriteLine("\n[Savepoints — partial rollback within transaction]");

        await using var txn = await d.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var farmId = await txn.InsertAndGetIdAsync<Farm, int>(
                new Farm { Name = "TX Farm C", Location = "Pretoria", HectareSize = 75, TenantId = "txn-demo" });

            Console.WriteLine($"  Inserted Farm C (id={farmId}) — will keep");
            await txn.SavepointAsync("before_field");

            try
            {
                await txn.InsertAsync(
                    new Field { Name = "Bad Field", FarmId = farmId, AreaHectares = 0, CropType = "None", TenantId = "txn-demo" });

                throw new InvalidOperationException("Field data invalid — rolling back to savepoint");
            }
            catch (InvalidOperationException ex)
            {
                await txn.RollbackToAsync("before_field");
                Console.WriteLine($"  Rolled back to savepoint: {ex.Message}");
            }

            await txn.CommitAsync();
        }
        catch
        {
            await txn.RollbackAsync();
            throw;
        }

        var farmC = await d.Query<Farm>().Where(f => f.Name == "TX Farm C").FirstOrDefaultAsync();
        var fieldsAfter = await d.Query<Field>().CountAsync();
        Console.WriteLine($"  Farm C persisted: {farmC != null}");
        Console.WriteLine($"  Fields (bad field rolled back): {fieldsAfter}");

        // ── Bulk operations in a transaction ──────────────────────────────────
        Console.WriteLine("\n[Bulk insert inside transaction]");

        await d.TransactionAsync(async tx =>
        {
            var farmId = await tx.InsertAndGetIdAsync<Farm, int>(
                new Farm { Name = "Bulk Farm", Location = "Johannesburg", HectareSize = 500, TenantId = "txn-demo" });

            var fields = new[]
            {
                new Field { Name = "Block 1", FarmId = farmId, AreaHectares = 100, CropType = "Soy",    TenantId = "txn-demo" },
                new Field { Name = "Block 2", FarmId = farmId, AreaHectares = 200, CropType = "Maize",  TenantId = "txn-demo" },
                new Field { Name = "Block 3", FarmId = farmId, AreaHectares = 200, CropType = "Canola", TenantId = "txn-demo" },
            };
            await tx.BulkInsertAsync(fields);
            Console.WriteLine($"  Bulk-inserted 3 fields for farm id={farmId}");
        });

        var totalFarms = await d.Query<Farm>().CountAsync();
        var totalFields = await d.Query<Field>().CountAsync();
        Console.WriteLine($"  Final totals: {totalFarms} farms, {totalFields} fields");
    }
}
