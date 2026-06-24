/// <summary>Extension methods for adding Polly resilience to Azure Key Vault clients.</summary>
public static class PollyAzureKeyVaultExtensions
{
    /// <summary>Wraps a <see cref="SecretClient"/> with the given <see cref="ResiliencePipeline"/>.</summary>
    public static ResilientSecretClient WithPolly(
        this SecretClient client,
        ResiliencePipeline pipeline)
        => new(client, pipeline);

    /// <summary>Wraps a <see cref="SecretClient"/> with a pipeline built by <paramref name="configure"/>.</summary>
    public static ResilientSecretClient WithPolly(
        this SecretClient client,
        Action<ResiliencePipelineBuilder> configure)
    {
        var builder = new ResiliencePipelineBuilder();
        configure(builder);
        return new(client, builder.Build());
    }
}
