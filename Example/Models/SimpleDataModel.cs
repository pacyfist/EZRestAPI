namespace Example;

[EZRestAPI.EZRestAPIModel("SimpleData", "SimpleDataPlural")]
public class SimpleDataModel
{
    public int Id { get; set; }

    public required int IntegerProperty { get; set; }

    public required double DoubleProperty { get; set; }

    public required string StringProperty { get; set; }

    public required DateTimeOffset DateTimeOffsetProperty { get; set; }
}
