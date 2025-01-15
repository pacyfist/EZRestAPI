namespace Example.Models;

[EZRestAPI.Model("SimpleData", "SimpleDataPlural")]
public partial class SimpleDataModel
{
    public required int IntegerProperty { get; set; }

    public required double DoubleProperty { get; set; }

    public required string? StringProperty { get; set; }

    public required DateTimeOffset DateTimeOffsetProperty { get; set; }
}
