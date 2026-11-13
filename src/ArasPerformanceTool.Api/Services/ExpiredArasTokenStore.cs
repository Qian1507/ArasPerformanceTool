
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ArasPerformanceTool.Api.Services;

public sealed class ExpiredArasTokenStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _expired =
        new(StringComparer.Ordinal);

    private static string GetKey(string token)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(token));

        return Convert.ToHexString(bytes);
    }

    public void MarkExpired(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        _expired[GetKey(token)] = DateTimeOffset.UtcNow;
    }

    public bool IsExpired(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        return _expired.ContainsKey(GetKey(token));
    }
}
