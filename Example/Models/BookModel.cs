namespace Example.Models;

using System.ComponentModel.DataAnnotations;

[EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.All)]
public partial class BookModel
{
    [MaxLength(255)]
    public required string Title { get; set; }

    public required int AuthorId { get; set; }
}
