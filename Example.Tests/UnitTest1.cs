namespace Example.Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        var model = new SimpleDataModel
        {
            Whatever = 1337,
            Forever = "Hello, World!"
        };

        Assert.Equal(0, model.Id);
    }
}
