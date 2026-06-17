using FluentORM.SqlServerDemo;

var connectionString =
    Environment.GetEnvironmentVariable("SQL_SERVER_CONNECTION_STRING")
    ?? "Server=localhost;Database=FluentOrmDemo;Integrated Security=true;TrustServerCertificate=true;";

Console.WriteLine("FluentORM — SQL Server Demo");
Console.WriteLine($"Connecting to: {MaskPassword(connectionString)}");
Console.WriteLine();

DemoDb demoDb;
try
{
    demoDb = await DemoDb.CreateAsync(connectionString);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not connect to SQL Server: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Set the SQL_SERVER_CONNECTION_STRING environment variable, e.g.:");
    Console.Error.WriteLine("  $env:SQL_SERVER_CONNECTION_STRING = \"Server=.;Database=FluentOrmDemo;Integrated Security=true;TrustServerCertificate=true;\"");
    return 1;
}

await using (demoDb)
{
    var db = demoDb.Db;

    await Demo01_BasicCRUD.RunAsync(db);
    await Demo02_Queries.RunAsync(db);
    await Demo03_Transactions.RunAsync(db);
    await Demo04_BulkAndRawSql.RunAsync(db);
}

Console.WriteLine("\n═══════════════════════════════════════");
Console.WriteLine(" All SQL Server demos completed!");
Console.WriteLine("═══════════════════════════════════════");
return 0;

static string MaskPassword(string cs)
{
    var idx = cs.IndexOf("Password=", StringComparison.OrdinalIgnoreCase);
    if (idx < 0) return cs;
    var end = cs.IndexOf(';', idx);
    return cs[..idx] + "Password=***" + (end < 0 ? "" : cs[end..]);
}
