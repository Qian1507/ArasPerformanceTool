using ArasPerformanceTool.Engine.Services;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Storage;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Api.Services;
using QuestPDF.Infrastructure;
using ArasPerformanceTool.Api.Reporting;

// Community license for development and evaluation.
// Reassess licensing before company production deployment.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Register HttpClient
builder.Services.AddHttpClient("Aras");

// Register FileResultStore with the configured report directory.
builder.Services.AddSingleton<FileResultStore>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var environment = sp.GetRequiredService<IWebHostEnvironment>();

    var dataDirectory = configuration["Storage:DataDirectory"];

    if (string.IsNullOrWhiteSpace(dataDirectory))
    {
        throw new InvalidOperationException(
            "Storage data directory is not configured.");
    }

    var dataRoot = Path.IsPathRooted(dataDirectory)
        ? Path.GetFullPath(dataDirectory)
        : Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            dataDirectory));

    var reportDirectory = Path.Combine(dataRoot, "reports");

    return new FileResultStore(reportDirectory);
});

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

// Register benchmark orchestration services
builder.Services.AddScoped<StandardBenchmarkService>();
builder.Services.AddScoped<CustomAmlBenchmarkService>();

// Register test data manager for dependency injection
builder.Services.AddScoped<TestDataManager>();
// Register the TestDataCleanupService for dependency injection
builder.Services.AddTransient<TestDataCleanupService>();

// Register CRUD workloads
builder.Services.AddTransient<CreateWorkload>();
builder.Services.AddTransient<ReadWorkload>();
builder.Services.AddTransient<UpdateWorkload>();
builder.Services.AddTransient<DeleteWorkload>();

// Register the RunExecutionCoordinator as a singleton
builder.Services.AddSingleton<RunExecutionCoordinator>();

//register the PdfReportGenerator for dependency injection
builder.Services.AddTransient<PdfReportGenerator>();

// Server-side session for Aras authentication.
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);

    options.Cookie.Name = ".ArasPerformanceTool.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Protect state-changing requests against CSRF.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSession();
// Serve the default page (index.html) when accessing the root URL
app.UseDefaultFiles();

// Enable serving static files (HTML, CSS, JavaScript) from wwwroot
app.UseStaticFiles();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program { }