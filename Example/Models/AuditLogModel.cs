namespace Example.Models;

/// <summary>
/// Endpoints.None (the default): registered in the DbContext and nothing else.
/// No repository, no DTOs, no routes — persistence without an API.
/// </summary>
[EZRestAPI.Model("AuditLog", "AuditLogs")]
public partial class AuditLogModel
{
    public required string Message { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }
}
