namespace Example.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

public class RepositoryTests : IAsyncLifetime
{
    private MsSqlContainer container = null!;

    private CustomDbContext context = null!;
    private SimpleDataRepository service = null!;

    public async Task InitializeAsync()
    {
        container = new MsSqlBuilder().Build();
        await container.StartAsync();

        var connectionString = container.GetConnectionString();

        var serviceCollection = new ServiceCollection()
            .AddDbContext<CustomDbContext>(o => o.UseSqlServer(connectionString))
            .AddSingleton<SimpleDataRepository>();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        context = serviceProvider.GetRequiredService<CustomDbContext>();
        service = serviceProvider.GetRequiredService<SimpleDataRepository>();

        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await container.StopAsync();
    }



    [Fact]
    public async Task SimpleModel_CreateDelete_Counts()
    {
        Assert.Equal(0, context.SimpleDataPlural.Count());

        var id = await service.CreateAsync(
            integerProperty: 1,
            doubleProperty: 1.1,
            stringProperty: "Test",
            dateTimeOffsetProperty: DateTimeOffset.Now,
            CancellationToken.None);

        Assert.Equal(1, context.SimpleDataPlural.Count());

        await service.DeleteAsync(
            id: id,
            CancellationToken.None);

        Assert.Equal(0, context.SimpleDataPlural.Count());
    }

    [Fact]
    public async Task SimpleModel_CreateUpdateReadDelete_Counts()
    {
        Assert.Equal(0, context.SimpleDataPlural.Count());

        var id = await service.CreateAsync(
            integerProperty: 1,
            doubleProperty: 1.1,
            stringProperty: "Test",
            dateTimeOffsetProperty: DateTimeOffset.Now,
            CancellationToken.None);

        Assert.Equal("Test", context.SimpleDataPlural.First().StringProperty);

        await service.UpdateAsync(
            id: id,
            integerProperty: 2,
            doubleProperty: 2.2,
            stringProperty: "Test2",
            dateTimeOffsetProperty: DateTimeOffset.Now,
            CancellationToken.None);

        Assert.Equal("Test2", context.SimpleDataPlural.First().StringProperty);

        var result = await service.ReadAsync(
            id: id,
            CancellationToken.None);

        Assert.Equal("Test2", result.StringProperty);

        await service.DeleteAsync(
            id: id,
            CancellationToken.None);

        Assert.Equal(0, context.SimpleDataPlural.Count());
    }
}
