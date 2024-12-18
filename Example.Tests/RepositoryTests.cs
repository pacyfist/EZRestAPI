namespace Example.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class RepositoryTests
{
    readonly IServiceCollection serviceCollection;

    public RepositoryTests()
    {
        serviceCollection = new ServiceCollection()
            .AddDbContext<CustomDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()))
            .AddSingleton<SimpleDataRepository>();
    }

    [Fact]
    public async Task CreateSimpleModel_ModelExists()
    {
        // Arrange
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<CustomDbContext>();
        var service = serviceProvider.GetRequiredService<SimpleDataRepository>();

        // Act
        await service.CreateAsync(
            IntegerProperty: 1,
            DoubleProperty: 1.1,
            StringProperty: "Test",
            DateTimeOffsetProperty: DateTimeOffset.Now,
            CancellationToken.None);

        Assert.Equal(1, context.SimpleDataPlural.Count());
    }
}
