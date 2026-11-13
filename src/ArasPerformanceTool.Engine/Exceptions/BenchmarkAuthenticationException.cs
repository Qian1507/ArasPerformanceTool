using System.Net;


namespace ArasPerformanceTool.Engine.Exceptions;

public sealed class BenchmarkAuthenticationException
    : HttpRequestException
{
    public BenchmarkReport PartialReport { get; }

    public BenchmarkAuthenticationException(
        BenchmarkReport partialReport,
        HttpRequestException innerException)
        : base(
            "Aras authentication is no longer valid. Please sign in again.",
            innerException,
            HttpStatusCode.Unauthorized)
    {
        PartialReport = partialReport;
    }
}