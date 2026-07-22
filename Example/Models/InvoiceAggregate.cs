namespace Example.Models;

using System.Collections.Generic;

// A child ENTITY (not a value object): mapped as an OwnsMany owned type inside
// the Invoice aggregate and embedded in reads as InvoiceLineDto.
[EZRestAPI.Nested("InvoiceLine")]
public class InvoiceLine
{
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public required string Sku { get; set; }

    public required int Quantity { get; set; }

    public required decimal UnitPrice { get; set; }
}

// An aggregate with an OwnsMany child-entity collection (IReadOnlyList over a
// private backing field), contrasting Order's IReadOnlyList<string> projection.
[EZRestAPI.Aggregate("Invoice", "Invoices")]
public partial class Invoice
{
    private Invoice() { } // EF materialization ctor

    [EZRestAPI.Factory]
    public static Invoice Open(CustomerRef billTo)
    {
        return new Invoice { BillTo = billTo, Status = "Open" };
    }

    public CustomerRef BillTo { get; private set; } = null!;

    public string Status { get; private set; } = "";

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    private readonly List<InvoiceLine> _lines = new();

    [EZRestAPI.Command]
    public void AddLine(string sku, int quantity, decimal unitPrice)
    {
        if (quantity <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(quantity));
        }

        _lines.Add(
            new InvoiceLine
            {
                Sku = sku,
                Quantity = quantity,
                UnitPrice = unitPrice,
            }
        );
    }
}
