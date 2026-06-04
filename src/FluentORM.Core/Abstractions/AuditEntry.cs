using System;
using FluentORM.Core.Attributes;

namespace FluentORM.Core.Abstractions;

[Table("__AuditEntries")]
public class AuditEntry
{
    [PrimaryKey(autoIncrement: true)]
    public long Id { get; set; }

    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string PrimaryKey { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
}
