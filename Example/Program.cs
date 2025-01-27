using Example;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContextFactory<Example.CustomDbContext>(o =>
    o.UseSqlServer("Server=localhost;Database=example;User=sa;Password=Password123;"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var group = app.MapGroup("/simpledatamodels");

group.MapGet("/create", async ([FromServices]SimpleDataRepository repository, CancellationToken cancellationToken) =>
{
    return await repository.CreateAsync(
        integerProperty: 1,
        doubleProperty: 1.1,
        stringProperty: "Test",
        dateTimeOffsetProperty: DateTimeOffset.Now,
        cancellationToken);
})
.WithName("GetWeatherForecast");


app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
