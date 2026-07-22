namespace Example.Models;

using System.ComponentModel.DataAnnotations;

// Rich validation annotations beyond [MaxLength]: each is copied onto the
// generated CreateRegistrationRequest and enforced by the validation filter,
// so a bad field yields a 422 problem+json with an RFC 9457 `errors` map.
[EZRestAPI.Model("Registration", "Registrations")]
public partial class RegistrationModel
{
    [Required]
    [StringLength(32, MinimumLength = 3)]
    public required string Username { get; set; }

    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Range(18, 120)]
    public int Age { get; set; }

    [RegularExpression(@"^\+?[0-9]{7,15}$")]
    public string? PhoneNumber { get; set; }

    [Url]
    public string? Website { get; set; }
}
