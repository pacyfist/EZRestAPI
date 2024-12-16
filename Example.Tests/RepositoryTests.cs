namespace Example.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class RepositoryTests
{
    [Fact]
    public async Task CreateSimpleModel_ModelExists()
    {
        // Arrange
        var ServiceProvider = new ServiceCollection()
            .AddDbContext<CustomDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()))
            .AddSingleton<SimpleDataRepository>()
            .BuildServiceProvider();

        var context = ServiceProvider.GetRequiredService<CustomDbContext>();
        var service = ServiceProvider.GetRequiredService<SimpleDataRepository>();

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
