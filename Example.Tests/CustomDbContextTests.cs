using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Example.Tests;

public class CustomDbContextTests
{
    readonly IServiceCollection serviceCollection;

    public CustomDbContextTests()
    {
        serviceCollection = new ServiceCollection()
            .AddDbContext<CustomDbContext>(o => o.UseInMemoryDatabase("Test"));
    }

    [Fact]
    public void AddSimpleModel()
    {
        // Arrange
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<CustomDbContext>();

        // Act
        context.SimpleDataPlural.Add(new()
        {
            IntegerProperty = 1,
            DoubleProperty = 1.1,
            StringProperty = "Test",
            DateTimeOffsetProperty = DateTimeOffset.Now
        });
        context.SaveChanges();

        // Assert
        Assert.Equal(1, context.SimpleDataPlural.Count());
    }
}
