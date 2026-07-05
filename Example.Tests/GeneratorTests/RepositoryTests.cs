namespace Example.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[Collection("MsSql")]
public class RepositoryTests
{
    private readonly CustomDbContext context;
    private readonly SimpleDataRepository service;

    public RepositoryTests(MsSqlContainerFixture fixture)
    {
        var serviceCollection = new ServiceCollection()
            .AddDbContext<CustomDbContext>(o => o.UseSqlServer(fixture.ConnectionString))
            .AddSingleton<SimpleDataRepository>();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        context = serviceProvider.GetRequiredService<CustomDbContext>();
        service = serviceProvider.GetRequiredService<SimpleDataRepository>();
    }

    [Fact]
    public async Task SimpleModel_CreateDelete_Counts()
    {
        Assert.Equal(0, context.SimpleDataPlural.Count());

        var id = await service.CreateAsync(
            new CreateSimpleDataRequest
            {
                IntegerProperty = 1,
                DoubleProperty = 1.1,
                StringProperty = "Test",
                DateTimeOffsetProperty = DateTimeOffset.Now,
            },
            CancellationToken.None
        );

        Assert.Equal(1, context.SimpleDataPlural.Count());

        await service.DeleteAsync(id: id, CancellationToken.None);

        Assert.Equal(0, context.SimpleDataPlural.Count());
    }

    [Fact]
    public async Task SimpleModel_CreateUpdateReadDelete_Counts()
    {
        Assert.Equal(0, context.SimpleDataPlural.Count());

        var id = await service.CreateAsync(
            new CreateSimpleDataRequest
            {
                IntegerProperty = 1,
                DoubleProperty = 1.1,
                StringProperty = "Test",
                DateTimeOffsetProperty = DateTimeOffset.Now,
            },
            CancellationToken.None
        );

        Assert.Equal("Test", context.SimpleDataPlural.First().StringProperty);

        await service.UpdateAsync(
            id,
            new UpdateSimpleDataRequest
            {
                Id = id,
                IntegerProperty = 2,
                DoubleProperty = 2.2,
                StringProperty = "Test2",
                DateTimeOffsetProperty = DateTimeOffset.Now,
            },
            CancellationToken.None
        );

        Assert.Equal("Test2", context.SimpleDataPlural.First().StringProperty);

        var result = await service.ReadAsync(id: id, CancellationToken.None);

        Assert.Equal("Test2", result?.StringProperty);

        await service.DeleteAsync(id: id, CancellationToken.None);

        Assert.Equal(0, context.SimpleDataPlural.Count());
    }
}
