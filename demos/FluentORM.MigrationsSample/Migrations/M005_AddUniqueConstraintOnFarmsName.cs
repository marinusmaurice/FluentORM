using FluentORM.Core.Attributes;
using FluentORM.MigrationsSample;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;

namespace FluentORM.MigrationsSample.Migrations;

[MigrationAttribute(20240601006, "add_unique_index_on_farms_name")]
public class M005_AddUniqueConstraintOnFarmsName : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        // AddUniqueIndex produces a deterministic name (UIX_{table}_{columns}),
        // unlike AddUniqueConstraint which generates a random suffix — that
        // makes it droppable later, so the migration stays reversible.
        schema.AddUniqueIndex<Farm>(f => f.Name);
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropIndex<Farm>("UIX_Farms_Name");
    }
}
