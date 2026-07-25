namespace Example.Models;

/// <summary>
/// Endpoints.Crud: all five flat verbs and no nested group, even though
/// AuditLogId makes this a child of AuditLog. The parent is Endpoints.None,
/// which still gives it the DbSet this model's foreign key points at.
/// </summary>
[EZRestAPI.Model("AuditNote", "AuditNotes", Endpoints = EZRestAPI.Endpoints.Crud)]
public partial class AuditNoteModel
{
    public required string Text { get; set; }

    public required int AuditLogId { get; set; }
}
