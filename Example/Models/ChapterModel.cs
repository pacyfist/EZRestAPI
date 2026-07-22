namespace Example.Models;

using System.ComponentModel.DataAnnotations;

// Third level of a chain: Author -> Book -> Chapter. Chapter's parent (Book) is
// itself a child of Author.
[EZRestAPI.Model("Chapter", "Chapters")]
public partial class ChapterModel
{
    [MaxLength(255)]
    public required string Heading { get; set; }

    public required int BookId { get; set; }
}
