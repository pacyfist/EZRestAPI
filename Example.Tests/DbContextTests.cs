namespace Example.Tests;

using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class DbContextTests
{
    readonly IServiceCollection serviceCollection;

    public DbContextTests()
    {
        serviceCollection = new ServiceCollection()
            .AddDbContext<CustomDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
    }

    [Fact]
    public void AddSimpleModel()
    {
        // Arrange
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<CustomDbContext>();

        // Act
        var simpleDataModel = new SimpleDataModelFaker();
        context.SimpleDataPlural.Add(simpleDataModel);
        context.SaveChanges();

        // Assert
        Assert.Equal(1, context.SimpleDataPlural.Count());
    }
}
