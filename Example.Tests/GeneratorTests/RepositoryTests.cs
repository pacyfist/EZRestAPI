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
        container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04"
        ).Build();
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
