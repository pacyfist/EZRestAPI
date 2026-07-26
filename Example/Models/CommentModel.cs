namespace Example.Models;

using System.ComponentModel.DataAnnotations;

[EZRestAPI.Nested("Comment")]
public class CommentModel
{
    [MaxLength(1024)]
    public required string Text { get; set; }

    public required IReadOnlyList<ReactionModel> Reactions { get; set; }
}
