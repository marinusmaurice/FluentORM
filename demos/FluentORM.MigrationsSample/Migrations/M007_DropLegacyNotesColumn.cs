using FluentORM.Core.Attributes;
using FluentORM.Core.Exceptions;
using FluentORM.MigrationsSample;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace FluentORM.MigrationsSample.Migrations;

// Destructive migrations require an explicit --allow-destructive opt-in at
// ApplyAsync() time, and they can document why Down() can't safely restore
// the data it removes.
[MigrationAttribute(20240602001, "drop_legacynotes_column")]
[DestructiveAttribute("Drops Farms.LegacyNotes — any data in that column is permanently lost.")]
public class M007_DropLegacyNotesColumn : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.DropColumn<Farm>(f => f.LegacyNotes);
    }

    public override void Down(SchemaBuilder schema)
    {
        throw new IrreversibleMigrationException(
            "LegacyNotes data was dropped and cannot be reconstructed.");
    }
}
