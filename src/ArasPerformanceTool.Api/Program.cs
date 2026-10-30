
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Register HttpClient
builder.Services.AddHttpClient("Aras");

// Register ArasClient with required configuration
builder.Services.AddTransient<IArasClient>(sp =>
{
    var httpClientFactory =
        sp.GetRequiredService<IHttpClientFactory>();

    var configuration =
        sp.GetRequiredService<IConfiguration>();

    var oauthUrl = configuration["Aras:OAuthUrl"]
        ?? throw new InvalidOperationException(
            "Aras OAuth URL is missing.");

    var database = configuration["Aras:Database"]
        ?? throw new InvalidOperationException(
            "Aras database is missing.");

    return new ArasClient(
        httpClientFactory.CreateClient("Aras"),
        oauthUrl,
        database);
});

// Register CRUD workloads
builder.Services.AddTransient<CreateWorkload>();
builder.Services.AddTransient<ReadWorkload>();
builder.Services.AddTransient<UpdateWorkload>();
builder.Services.AddTransient<DeleteWorkload>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serve the default page (index.html) when accessing the root URL
app.UseDefaultFiles();

// Enable serving static files (HTML, CSS, JavaScript) from wwwroot
app.UseStaticFiles();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program { }