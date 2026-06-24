public class PollyAzureKeyVaultServiceCollectionExtensionsTests
{
    private static readonly SecretClient _client =
        new(new Uri("https://fake-vault.vault.azure.net/"), new FakeTokenCredential());

    [Fact]
    public void AddPollyAzureKeyVault_RegistersResiliencePipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_client);
        services.AddPollyAzureKeyVault(p => p.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            ShouldHandle = KeyVaultTransientErrors.IsTransient,
        }));

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ResiliencePipeline>());
    }

    [Fact]
    public void AddPollyAzureKeyVault_RegistersResilientSecretClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_client);
        services.AddPollyAzureKeyVault(p => { });

        var provider = services.BuildServiceProvider();
        var resilient = provider.GetRequiredService<ResilientSecretClient>();

        Assert.NotNull(resilient);
        Assert.Same(_client, resilient.Inner);
    }

    [Fact]
    public void AddPollyAzureKeyVault_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_client);

        var result = services.AddPollyAzureKeyVault(p => { });

        Assert.Same(services, result);
    }
}
