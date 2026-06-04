using FluentORM.Demos;

// Initialize the demo database and run all demos
var demoDb = await DemoDb.CreateAsync();
await using (demoDb)
{
    var db = demoDb.Db;

    await Demo01_BasicCRUD.RunAsync(db);
    await Demo02_Queries.RunAsync(db);
    await Demo03_MultiTenancy.RunAsync(db);
    await Demo04_Mutations.RunAsync(db);
    await Demo05_Transactions.RunAsync(db);
    await Demo06_OptimisticConcurrency.RunAsync(db);
    await Demo07_ComplexQueries.RunAsync(db);
    await Demo08_AggregatesAndCombos.RunAsync(db);
}

Console.WriteLine("\n═══════════════════════════════════════");
Console.WriteLine(" All demos completed!");
Console.WriteLine("═══════════════════════════════════════");
