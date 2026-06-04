using System;
using FluentORM.Core.Attributes;
using FluentORM.Migrations.Schema;

namespace FluentORM.Migrations.Engine;

public abstract class Migration
{
    public abstract void Up(SchemaBuilder schema);
    public abstract void Down(SchemaBuilder schema);
}
