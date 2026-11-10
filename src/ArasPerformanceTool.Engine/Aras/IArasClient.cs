namespace ArasPerformanceTool.Engine.Aras;

public interface IArasClient
{
    /// <summary>
    /// Acquires an OAuth 2.0 access token using resource owner password credentials.
    /// </summary>
    /// <param name="username">Aras user login name.</param>
    /// <param name="password">Aras plain text password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bearer access token string.</returns>
    /// 
    /// // Legacy authentication method.
    // Uses the default Aras database configured in appsettings.json.
    // Kept for backward compatibility with existing benchmarks and tests.
    Task<string> GetAccessTokenAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    // Authentication with a user-selected Aras database.
    // Used by the Login API to authenticate against the database
    // selected from the dynamically loaded database list.
    Task<string> GetAccessTokenAsync(
        string username,
        string password,
        string database,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an AML query wrapped in a SOAP envelope against the InnovatorServer.
    /// </summary>
    /// <param name="serverUrl">Target InnovatorServer endpoint URL.</param>
    /// <param name="accessToken">Valid Bearer access token.</param>
    /// <param name="aml">AML payload wrapped inside root AML tags.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw SOAP/AML XML response string.</returns>
    Task<string> ExecuteAmlAsync(
        string serverUrl,
        string accessToken,
        string aml,
        CancellationToken cancellationToken = default);
}