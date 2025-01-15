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
    public async Task SimpleModel_Create_Delete_CheckCounts()
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
    public async Task SimpleModel_Delete_IsDeleted()
    {
        // Arrange
        var model = new SimpleDataModelFaker().Generate();
        context.SimpleDataPlural.Add(model);
        context.SaveChanges();

        // Act
        await service.DeleteAsync(
            id: model.Id,
            CancellationToken.None);

        Assert.Equal(0, context.SimpleDataPlural.Count());
    }
}
