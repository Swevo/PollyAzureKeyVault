/// <summary>Dependency-injection extensions for <c>PollyAzureKeyVault</c>.</summary>
public static class PollyAzureKeyVaultServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="ResiliencePipeline"/> built by <paramref name="configure"/>
    /// and a transient <see cref="ResilientSecretClient"/> that wraps the
    /// <see cref="SecretClient"/> already registered in the DI container.
    /// </summary>
    public static IServiceCollection AddPollyAzureKeyVault(
        this IServiceCollection services,
        Action<ResiliencePipelineBuilder> configure)
    {
        var builder = new ResiliencePipelineBuilder();
        configure(builder);
        var pipeline = builder.Build();

        services.AddSingleton(pipeline);
        services.AddTransient<ResilientSecretClient>(sp =>
            sp.GetRequiredService<SecretClient>().WithPolly(pipeline));

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="SecretClient"/> for <paramref name="vaultUri"/>,
    /// then registers the resilience pipeline and <see cref="ResilientSecretClient"/>.
    /// </summary>
    public static IServiceCollection AddPollyAzureKeyVault(
        this IServiceCollection services,
        Uri vaultUri,
        Action<ResiliencePipelineBuilder> configure)
    {
        services.AddSingleton(new SecretClient(vaultUri, new Azure.Identity.DefaultAzureCredential()));
        return services.AddPollyAzureKeyVault(configure);
    }
}
