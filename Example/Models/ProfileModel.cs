namespace Example.Models;

using System.ComponentModel.DataAnnotations;

// A single (non-collection) owned reference: Address maps as OwnsOne in the
// anemic [Model] path, contrasting Post's owned *collection* of Comments.
[EZRestAPI.Nested("Address")]
public class AddressModel
{
    [MaxLength(255)]
    public required string Street { get; set; }

    [MaxLength(128)]
    public required string City { get; set; }

    [MaxLength(16)]
    public required string PostalCode { get; set; }
}

[EZRestAPI.Model("Profile", "Profiles")]
public partial class ProfileModel
{
    [MaxLength(128)]
    public required string DisplayName { get; set; }

    public required AddressModel Address { get; set; }
}
