namespace ArasPerformanceTool.Engine.Aras;

// Represents an explicit authentication rejection from Aras OAuth.
public sealed class ArasAuthenticationException : Exception
{
    public ArasAuthenticationException(string message)
        : base(message)
    {
    }
}