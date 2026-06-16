using FluentORM.Core.Attributes;
using FluentORM.MigrationsSample;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace FluentORM.MigrationsSample.Migrations;

[MigrationAttribute(20240601002, "create_fields_table_with_fk")]
public class M002_CreateFieldsTable : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.CreateTable<Field>(t =>
        {
            t.PrimaryKey(f => f.Id).AutoIncrement();
            t.Column(f => f.Name).NotNull().MaxLength(200);
            t.Column(f => f.FarmId).NotNull();
            t.Column(f => f.CropType).MaxLength(100);
        });

        schema.AddForeignKey<Field, Farm>(
            childCol: f => f.FarmId,
            parentCol: f => f.Id,
            onDelete: CascadeRule.Cascade);
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropTable<Field>();
    }
}
