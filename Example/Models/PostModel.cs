namespace Example.Models;

using System.ComponentModel.DataAnnotations;

[EZRestAPI.Model("Post", "Posts")]
public partial class PostModel
{
    [MaxLength(255)]
    public required string Title { get; set; }

    public required List<CommentModel> Comments { get; set; }
}
