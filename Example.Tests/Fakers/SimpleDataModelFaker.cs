using Bogus;

namespace Example.Tests;

public class SimpleDataModelFaker : Faker<SimpleDataModel>
{
    public SimpleDataModelFaker()
    {
        RuleFor(a => a.IntegerProperty, f => f.Random.Int());
        RuleFor(a => a.DoubleProperty, f => f.Random.Double());
        RuleFor(a => a.StringProperty, f => f.Random.String());
        RuleFor(a => a.DateTimeOffsetProperty, f => DateTimeOffset.Now);
    }
}
