using System;
using System.Threading.Tasks;
using FluentORM.Core.Attributes;
using FluentORM.Testing;
using Xunit;
using FluentAssertions;

namespace FluentORM.Sqlite.Tests;

[Table("Products")]
public class Product
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;

    [SoftDelete]
    public DateTime? DeletedAt { get; set; }
}

public class SqliteIntegrationTests : IAsyncDisposable
{
    private readonly DbTest _testDb;

    public SqliteIntegrationTests()
    {
        _testDb = DbTest.CreateAsync<object>().GetAwaiter().GetResult();
        SetupSchema().GetAwaiter().GetResult();
    }

    private async Task SetupSchema()
    {
        await _testDb.Db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Price REAL NOT NULL DEFAULT 0,
                TenantId TEXT NOT NULL,
                DeletedAt TEXT NULL
            )");
    }

    [Fact]
    public async Task CanInsertAndQueryEntity()
    {
        var tenantDb = _testDb.ForTenant("t1");
        var product = new Product { Name = "Widget", Price = 9.99m, TenantId = "t1" };
        await tenantDb.InsertAsync(product);

        var results = await tenantDb.Query<Product>().ToListAsync();

        results.Should().HaveCountGreaterThan(0);
        results.Should().Contain(p => p.Name == "Widget");
    }

    [Fact]
    public async Task TenantIsolation_PreventsOtherTenantData()
    {
        var db1 = _testDb.ForTenant("t2");
        var db2 = _testDb.ForTenant("t3");

        await db1.InsertAsync(new Product { Name = "T2 Product", Price = 1m, TenantId = "t2" });
        await db2.InsertAsync(new Product { Name = "T3 Product", Price = 2m, TenantId = "t3" });

        var t2Results = await db1.Query<Product>().ToListAsync();
        t2Results.Should().OnlyContain(p => p.TenantId == "t2");
    }

    [Fact]
    public async Task SoftDelete_SetsDeletedAt()
    {
        var tenantDb = _testDb.ForTenant("t4");
        var product = new Product { Name = "To Delete", Price = 5m, TenantId = "t4" };
        await tenantDb.InsertAsync(product);

        var inserted = await tenantDb.Query<Product>().FirstOrDefaultAsync();
        inserted.Should().NotBeNull();

        await tenantDb.DeleteAsync<Product>(inserted!.Id);

        var afterDelete = await tenantDb.Query<Product>().ToListAsync();
        afterDelete.Should().NotContain(p => p.Name == "To Delete");
    }

    public async ValueTask DisposeAsync() => await _testDb.DisposeAsync();
}
