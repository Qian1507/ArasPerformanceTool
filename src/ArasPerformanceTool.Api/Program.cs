
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking.Workloads; 

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddHttpClient<IArasClient, ArasClient>();

builder.Services.AddTransient<CreateWorkload>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRouting();
app.MapControllers();

app.Run();


