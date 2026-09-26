using HaulCycle.Api.Data;
using HaulCycle.Api.Endpoints;
using Scalar.AspNetCore;

const string DevCorsPolicy = "DevClient";

var builder = WebApplication.CreateBuilder(args);

// Fail fast on a missing connection string, same convention as the data generator
// (HAUL_DB_CONN there, HAUL_API_DB_CONN here - the API connects as a separate,
// read-only login, never as sa).
var connectionString = Environment.GetEnvironmentVariable("HAUL_API_DB_CONN");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("HAUL_API_DB_CONN is not set.");
    Console.Error.WriteLine("Example (local Docker SQL Server, read-only haul_api login):");
    Console.Error.WriteLine("  Server=localhost,1433;Database=HaulCycleInsights;User Id=haul_api;Password=<from .env>;TrustServerCertificate=True");
    return 1;
}

DapperTypeHandlerRegistration.RegisterAll();

builder.Services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));
builder.Services.AddScoped<HaulCycleQueries>();

builder.Services.AddOpenApi();

builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("DataEndpoints", policy => policy.Expire(TimeSpan.FromSeconds(30)).SetVaryByQuery("from", "to", "shift"));
});

builder.Services.AddProblemDetails();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(DevCorsPolicy, policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
    });
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseCors(DevCorsPolicy);
}

app.UseOutputCache();

app.MapHealthEndpoints();
app.MapMetaEndpoints();
app.MapFleetEndpoints();
app.MapTruckEndpoints();
app.MapRouteEndpoints();
app.MapBottleneckEndpoints();
app.MapScheduleEndpoints();

app.Run();
return 0;
