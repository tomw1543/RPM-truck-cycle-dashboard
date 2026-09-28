using HaulCycle.Api.Data;
using HaulCycle.Api.Endpoints;
using Scalar.AspNetCore;

const string CorsPolicy = "Client";

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

// Allowed browser origins for the deployed frontend, read from HAUL_API_CORS_ORIGINS
// (comma-separated, trimmed, empties and trailing slashes dropped). In Development this
// merges with the fixed Vite dev server origin below; outside Development, an unset or
// empty env var means no allowed origins - CORS stays closed rather than crashing.
var corsOriginsFromEnv = (Environment.GetEnvironmentVariable("HAUL_API_CORS_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Where(origin => origin.Length > 0)
    .ToArray();

var allowedOrigins = builder.Environment.IsDevelopment()
    ? corsOriginsFromEnv.Append("http://localhost:5173").Distinct().ToArray()
    : corsOriginsFromEnv;

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET")
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Scalar/OpenAPI are available in every environment - the deployed API has no other
// interactive docs UI, and there is nothing in the API surface sensitive enough to hide
// in Production (it is read-only and fronted by a public dashboard anyway).
app.MapOpenApi();
app.MapScalarApiReference();

app.UseCors(CorsPolicy);

app.UseOutputCache();

app.MapHealthEndpoints();
app.MapMetaEndpoints();
app.MapFleetEndpoints();
app.MapTruckEndpoints();
app.MapRouteEndpoints();
app.MapBottleneckEndpoints();
app.MapScheduleEndpoints();
app.MapOptimiserEndpoints();
app.MapLoaderEndpoints();

app.Run();
return 0;
