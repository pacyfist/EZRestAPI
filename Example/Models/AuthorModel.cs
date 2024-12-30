namespace Example;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[EZRestAPI.Model("Author", "Authors")]
public class AuthorModel
{
    [Key]
    public int Id { get; set; }

    // String Types
    [MaxLength(255)]
    public required string FirstName { get; set; } // nvarchar(255)

    [MaxLength(255)]
    public string? MiddleName { get; set; } // nvarchar(255) Nullable

    public required string Biography { get; set; } // nvarchar(max)

    public string? Details { get; set; } //nvarchar(max) Nullable

    // Integer Types
    public byte Age { get; set; } // tinyint

    public short OrderCount { get; set; } // smallint

    public int ItemsCount { get; set; } // int

    public long PartsCount { get; set; } // bigint

    // Decimal Types
    public decimal TotalPrice { get; set; } // decimal(18,2)

    [Column(TypeName = "decimal(18,4)")]
    public decimal ProductPrice { get; set; } // decimal(18,4)

    // Floating Point Types
    public float WeightInKilograms { get; set; } // real

    public double ConversionRate { get; set; } // float

    // Boolean Type
    public bool IsActive { get; set; } // bit

    // Date and Time Types
    public DateTime DateOfBirth { get; set; } // datetime2

    public DateTimeOffset LastLoginTime { get; set; } // datetimeoffset

    public TimeSpan WorkDayStartTime { get; set; } // time

    // Unique Identifier Type
    public Guid OrderId { get; set; } // uniqueidentifier

    // Binary Type
    [MaxLength(512)]
    public byte[] ProfilePicture { get; set; } // varbinary(512)

    public byte[]? ImageData { get; set; } // varbinary(max)

    //String types that are usually not needed, but added for completeness
    [Column(TypeName = "char(32)")]
    public string? FixedLengthCharacter { get; set; } //char(32)

    [Column(TypeName = "nchar(32)")]
    public string? FixedLengthString { get; set; } //nchar(32)
}