/// <summary>
/// Pre-built Polly <see cref="PredicateBuilder"/> for transient Azure Key Vault errors.
/// Covers throttling (429), service unavailability (503), gateway timeouts (504),
/// and transient network-level exceptions.
/// </summary>
public static class KeyVaultTransientErrors
{
    /// <summary>
    /// HTTP status codes returned by Azure Key Vault that indicate a transient failure safe to retry.
    /// </summary>
    public static readonly IReadOnlySet<int> StatusCodes = new HashSet<int>
    {
        429, // TooManyRequests — Key Vault throttle limit reached
        503, // ServiceUnavailable — Key Vault maintenance or regional outage
        504, // GatewayTimeout — proxy or load balancer timed out
    };

    /// <summary>
    /// A <see cref="PredicateBuilder"/> that handles:
    /// <list type="bullet">
    ///   <item><see cref="RequestFailedException"/> with a status code in <see cref="StatusCodes"/> (429, 503, 504)</item>
    ///   <item><see cref="HttpRequestException"/> — network-level failure</item>
    ///   <item><see cref="TaskCanceledException"/> — timeout or cancellation during transit</item>
    /// </list>
    /// Assign to <c>ShouldHandle</c> on any Polly strategy.
    /// </summary>
    public static readonly PredicateBuilder IsTransient =
        (PredicateBuilder)new PredicateBuilder()
            .Handle<RequestFailedException>(ex => StatusCodes.Contains(ex.Status))
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>();
}
