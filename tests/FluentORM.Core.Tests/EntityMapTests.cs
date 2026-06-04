using FluentORM.Core.Mapping;
using Xunit;
using FluentAssertions;

namespace FluentORM.Core.Tests;

public class EntityMapTests
{
    [Fact]
    public void EntityDescriptor_ReadsAttributesCorrectly()
    {
        var registry = new EntityMapRegistry();
        var descriptor = registry.GetDescriptor<Pest>();

        descriptor.TableName.Should().Be("Pests");
        descriptor.PrimaryKey.PropertyName.Should().Be("Id");
        descriptor.PrimaryKey.IsPrimaryKey.Should().BeTrue();
        descriptor.PrimaryKey.AutoIncrement.Should().BeTrue();
        descriptor.TenantKeyColumn.Should().NotBeNull();
        descriptor.TenantKeyColumn!.PropertyName.Should().Be("TenantId");
        descriptor.SoftDeleteColumn.Should().NotBeNull();
        descriptor.SoftDeleteColumn!.PropertyName.Should().Be("DeletedAt");
        descriptor.RowVersionColumn.Should().NotBeNull();
    }

    [Fact]
    public void EntityDescriptor_ResolvesByPropertyName()
    {
        var registry = new EntityMapRegistry();
        var descriptor = registry.GetDescriptor<Pest>();

        var col = descriptor.Resolve("Name");
        col.ColumnName.Should().Be("name");
    }

    [Fact]
    public void EntityDescriptor_InsertColumns_ExcludesAutoIncrementPK()
    {
        var registry = new EntityMapRegistry();
        var descriptor = registry.GetDescriptor<Pest>();

        var insertCols = descriptor.InsertColumns;
        insertCols.Should().NotContain(c => c.IsPrimaryKey && c.AutoIncrement);
    }

    [Fact]
    public void EntityDescriptor_ComputedColumns_AreRecognized()
    {
        var registry = new EntityMapRegistry();
        var descriptor = registry.GetDescriptor<Pest>();

        descriptor.ComputedColumns.Should().Contain(c => c.PropertyName == "DisplayLabel");
    }
}
