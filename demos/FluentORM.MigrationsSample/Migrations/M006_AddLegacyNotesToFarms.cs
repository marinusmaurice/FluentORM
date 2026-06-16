using FluentORM.Core.Attributes;
using FluentORM.MigrationsSample;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace FluentORM.MigrationsSample.Migrations;

[MigrationAttribute(20240601005, "add_legacynotes_to_farms")]
public class M006_AddLegacyNotesToFarms : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.AddColumn<Farm>(f => f.LegacyNotes).Nullable().MaxLength(500);
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropColumn<Farm>(f => f.LegacyNotes);
    }
}
