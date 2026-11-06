using ArasPerformanceTool.Engine.Models;
// Request for a managed Custom AML CRUD benchmark.
public sealed record CustomAmlCrudRequest(
    string CreateAml,
    string ReadAml,
    string UpdateAml,
    string DeleteAml,
    int Iterations = 1);