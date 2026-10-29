namespace ArasPerformanceTool.Engine.Exceptions;

public class ArasException : Exception
{
    public string? FaultCode { get; }
    public string? FaultString { get; }
    public string? Detail { get; }

    public bool IsNotFound => FaultCode == "0";

    public ArasException(
        string message,
        string? faultCode = null,
        string? faultString = null,
        string? detail = null)
        : base(message)
    {
        FaultCode = faultCode;
        FaultString = faultString;
        Detail = detail;
    }
}