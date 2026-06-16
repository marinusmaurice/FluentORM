using FluentORM.Core.Attributes;
using FluentORM.MigrationsSample;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace FluentORM.MigrationsSample.Migrations;

[MigrationAttribute(20240601001, "create_farms_table")]
public class M001_CreateFarmsTable : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.CreateTable<Farm>(t =>
        {
            t.PrimaryKey(f => f.Id).AutoIncrement();
            t.Column(f => f.Name).NotNull().MaxLength(200);
            t.Column(f => f.Location).MaxLength(200);
        });
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropTable<Farm>();
    }
}
