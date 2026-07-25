namespace Example.Models;

[EZRestAPI.Model("SimpleData", "SimpleDataPlural", Endpoints = EZRestAPI.Endpoints.All)]
public partial class SimpleDataModel
{
    public required int IntegerProperty { get; set; }

    public required double DoubleProperty { get; set; }

    public required string? StringProperty { get; set; }

    public required DateTimeOffset DateTimeOffsetProperty { get; set; }
}
