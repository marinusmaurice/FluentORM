using FluentORM.Core.Attributes;
using FluentORM.MigrationsSample;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace FluentORM.MigrationsSample.Migrations;

[MigrationAttribute(20240601004, "add_index_on_fields_croptype")]
public class M004_AddIndexOnFieldsCropType : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.AddIndex<Field>(f => f.CropType);
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropIndex<Field>("IX_Fields_CropType");
    }
}
