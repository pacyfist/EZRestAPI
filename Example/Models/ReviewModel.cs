namespace Example.Models;

using System.ComponentModel.DataAnnotations;

// Two foreign keys on one child: Review nests under BOTH /authors/{id}/reviews
// and /books/{id}/reviews, and is addressable flat at /reviews.
[EZRestAPI.Model("Review", "Reviews")]
public partial class ReviewModel
{
    [MaxLength(2048)]
    public required string Body { get; set; }

    public required int AuthorId { get; set; }

    public required int BookId { get; set; }
}
