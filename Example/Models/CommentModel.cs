namespace Example.Models;

using System.ComponentModel.DataAnnotations;

[EZRestAPI.Nested("Comment")]
public class CommentModel
{
    [MaxLength(1024)]
    public required string Text { get; set; }

    public required List<ReactionModel> Reactions { get; set; }
}
