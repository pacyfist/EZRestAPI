namespace Example.Models;

using System.Collections.Generic;

// A value object embedded in the Order aggregate (owned type, no independent
// route). Exposed in reads as its generated CustomerRefDto.
[EZRestAPI.Nested("CustomerRef")]
public class CustomerRef
{
    public required string Name { get; set; }

    public required string Email { get; set; }
}

// A DDD aggregate root: created through a factory, encapsulated behind private
// setters, with a get-only collection projection over a private backing field.
[EZRestAPI.Aggregate("Order", "Orders")]
public partial class Order
{
    // EF materialization constructor (standard DDD-with-EF pattern).
    private Order() { }

    [EZRestAPI.Factory]
    public static Order Place(CustomerRef customer)
    {
        return new Order { Customer = customer, Status = "Placed" };
    }

    public CustomerRef Customer { get; private set; } = null!;

    public string Status { get; private set; } = "";

    // Get-only projection over a private backing field: read into the DTO,
    // persisted via EF field access (PropertyAccessMode.Field).
    public IReadOnlyList<string> Lines => _lines;

    private readonly List<string> _lines = new();

    [EZRestAPI.Command("cancel")]
    public void Cancel()
    {
        if (Status == "Shipped")
        {
            throw new System.InvalidOperationException("Cannot cancel a shipped order.");
        }

        if (Status == "Cancelled")
        {
            throw new System.InvalidOperationException("Order is already cancelled.");
        }

        Status = "Cancelled";
    }

    [EZRestAPI.Command]
    public void AddLine(string sku, int quantity)
    {
        if (quantity <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(quantity));
        }

        _lines.Add($"{sku} x{quantity}");
    }
}
