namespace Example;

[EZRestAPI.EZRestAPIModel("SimpleData", "SimpleDatas")]
public class SimpleDataModel
{
    public int Id { get; set; }

    public required int Whatever { get; set; }

    public required string Forever { get; set; }
}
