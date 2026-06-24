/// <summary>
/// Wraps a <see cref="SecretClient"/> with a Polly v8 <see cref="ResiliencePipeline"/>,
/// applying retry, timeout, and circuit-breaker to every secret operation.
/// </summary>
public sealed class ResilientSecretClient(SecretClient client, ResiliencePipeline pipeline)
{
    /// <summary>The underlying <see cref="SecretClient"/>.</summary>
    public SecretClient Inner => client;

    /// <summary>Retrieves a secret, protected by the resilience pipeline.</summary>
    public Task<Response<KeyVaultSecret>> GetSecretAsync(
        string name,
        string? version = null,
        CancellationToken cancellationToken = default)
        => pipeline.ExecuteAsync(
            async ct => await client.GetSecretAsync(name, version, ct),
            cancellationToken).AsTask();

    /// <summary>Sets (creates or updates) a secret, protected by the resilience pipeline.</summary>
    public Task<Response<KeyVaultSecret>> SetSecretAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default)
        => pipeline.ExecuteAsync(
            async ct => await client.SetSecretAsync(name, value, ct),
            cancellationToken).AsTask();

    /// <summary>Begins deleting a secret, protected by the resilience pipeline.</summary>
    public Task<DeleteSecretOperation> StartDeleteSecretAsync(
        string name,
        CancellationToken cancellationToken = default)
        => pipeline.ExecuteAsync(
            async ct => await client.StartDeleteSecretAsync(name, ct),
            cancellationToken).AsTask();

    /// <summary>
    /// Executes any <see cref="SecretClient"/> operation, protected by the resilience pipeline.
    /// Use this for operations not covered by the typed overloads.
    /// </summary>
    public Task<T> ExecuteAsync<T>(
        Func<SecretClient, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
        => pipeline.ExecuteAsync(
            async ct => await operation(client, ct),
            cancellationToken).AsTask();
}
