namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

[Collection("MsSql")]
public class EndpointFlagsEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public EndpointFlagsEndpointTests(MsSqlContainerFixture fixture)
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Example"] = fixture.ConnectionString,
                        }
                    )
            )
        );

        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    /// <summary>
    /// "/exchangerates" IS a mapped path (GET is registered for it, since
    /// ExchangeRate is Endpoints.ReadOnly), just not for POST. ASP.NET Core's
    /// HttpMethodMatcherPolicy matches the path first and only then checks the
    /// method; when the path matches but no candidate accepts the request's
    /// verb, it short-circuits to 405 Method Not Allowed with an Allow header
    /// listing the verbs that ARE registered, rather than falling through to
    /// 404. 404 is reserved for paths with no matching endpoint at all, which
    /// is the case in <see cref="NoneModel_ExposesNoRoutes"/> and
    /// <see cref="CrudModel_ExposesFlatRoutesButNoNestedGroup"/> below (no
    /// "/auditlogs" or "/auditlogs/{id}/auditnotes" route is registered at
    /// all), but not here.
    /// </summary>
    [Fact]
    public async Task ReadOnlyModel_ExposesGetsAndRejectsWritesWith405()
    {
        var listResponse = await client.GetAsync("/exchangerates");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var writeResponse = await client.PostAsJsonAsync(
            "/exchangerates",
            new { Code = "USD", Rate = 1.0m }
        );
        Assert.Equal(HttpStatusCode.MethodNotAllowed, writeResponse.StatusCode);
        Assert.NotNull(writeResponse.Content.Headers.Allow);
        Assert.Contains("GET", writeResponse.Content.Headers.Allow);
    }

    /// <summary>
    /// Unlike the ReadOnly case above, no "/auditlogs" route is registered at
    /// all (AuditLog is Endpoints.None), so no path segment matches and
    /// routing falls through to a genuine 404.
    /// </summary>
    [Fact]
    public async Task NoneModel_ExposesNoRoutes()
    {
        var response = await client.GetAsync("/auditlogs");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// "/auditlogs/1/auditnotes" is a genuine 404, not a 405: AuditNote is
    /// Endpoints.Crud, which deliberately excludes Nested, so no route is
    /// registered under that path prefix for any verb.
    /// </summary>
    [Fact]
    public async Task CrudModel_ExposesFlatRoutesButNoNestedGroup()
    {
        var flatResponse = await client.GetAsync("/auditnotes");
        Assert.Equal(HttpStatusCode.OK, flatResponse.StatusCode);

        var nestedResponse = await client.GetAsync("/auditlogs/1/auditnotes");
        Assert.Equal(HttpStatusCode.NotFound, nestedResponse.StatusCode);
    }
}
