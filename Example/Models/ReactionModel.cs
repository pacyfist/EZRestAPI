namespace Example.Models;

using System.ComponentModel.DataAnnotations;

[EZRestAPI.Nested("Reaction")]
public class ReactionModel
{
    [MaxLength(16)]
    public required string Emoji { get; set; }
}
