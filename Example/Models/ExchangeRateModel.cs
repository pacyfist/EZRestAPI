namespace Example.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Endpoints.ReadOnly: GET the collection and GET an item, no writes.
/// </summary>
[EZRestAPI.Model("ExchangeRate", "ExchangeRates", Endpoints = EZRestAPI.Endpoints.ReadOnly)]
public partial class ExchangeRateModel
{
    [MaxLength(3)]
    public required string Code { get; set; }

    public required decimal Rate { get; set; }
}
