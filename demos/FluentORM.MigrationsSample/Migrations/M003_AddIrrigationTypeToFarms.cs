using FluentORM.Core.Attributes;
using FluentORM.MigrationsSample;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace FluentORM.MigrationsSample.Migrations;

[MigrationAttribute(20240601003, "add_irrigationtype_to_farms")]
public class M003_AddIrrigationTypeToFarms : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        // NOT NULL columns on an existing table need a Default, otherwise
        // FluentORM throws NotNullWithoutDefaultException — existing rows
        // would otherwise violate the constraint.
        schema.AddColumn<Farm>(f => f.IrrigationType)
              .MaxLength(50)
              .Default("None")
              .NotNull();
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropColumn<Farm>(f => f.IrrigationType);
    }
}
