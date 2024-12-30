namespace Example.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

public class RepositoryTests : IAsyncLifetime
{
    private IServiceCollection serviceCollection = null!;
    private MsSqlContainer container = null!;

    public async Task InitializeAsync()
    {
        container = new MsSqlBuilder().Build();
        await container.StartAsync();

        var connectionString = container.GetConnectionString();

        serviceCollection = new ServiceCollection()
            .AddDbContext<CustomDbContext>(o => o.UseSqlServer(connectionString))
            .AddSingleton<SimpleDataRepository>();
    }

    public async Task DisposeAsync()
    {
        await container.StopAsync();
    }



    [Fact]
    public async Task SimpleModel_Create_IsCreated()
    {
        // Arrange
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<CustomDbContext>();
        var service = serviceProvider.GetRequiredService<SimpleDataRepository>();

        await context.Database.EnsureCreatedAsync();

        // Act
        await service.CreateAsync(
            IntegerProperty: 1,
            DoubleProperty: 1.1,
            StringProperty: "Test",
            DateTimeOffsetProperty: DateTimeOffset.Now,
            CancellationToken.None);

        Assert.Equal(1, context.SimpleDataPlural.Count());
    }

    [Fact]
    public async Task SimpleModel_Delete_IsDeleted()
    {
        // Arrange
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var context = serviceProvider.GetRequiredService<CustomDbContext>();
        var service = serviceProvider.GetRequiredService<SimpleDataRepository>();

        await context.Database.EnsureCreatedAsync();

        var model = new SimpleDataModelFaker().Generate();
        context.SimpleDataPlural.Add(model);
        context.SaveChanges();

        // Act
        await service.DeleteAsync(
            Id: model.Id,
            CancellationToken.None);

        Assert.Equal(0, context.SimpleDataPlural.Count());
    }
}
