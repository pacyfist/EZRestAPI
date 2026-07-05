namespace Example.Tests;

using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

/// <summary>
/// One SQL Server container shared by every test class in the "MsSql"
/// collection; a per-test container costs tens of seconds each and booting
/// several at once can exhaust the host.
/// </summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private MsSqlContainer container = null!;

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04"
        ).Build();
        await container.StartAsync();

        ConnectionString = container.GetConnectionString();

        var options = new DbContextOptionsBuilder<CustomDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var context = new CustomDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();
    }
}

[CollectionDefinition("MsSql")]
public class MsSqlCollection : ICollectionFixture<MsSqlContainerFixture> { }
