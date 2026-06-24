public class PollyAzureKeyVaultExtensionsTests
{
    private static readonly SecretClient _client =
        new(new Uri("https://fake-vault.vault.azure.net/"), new FakeTokenCredential());

    private static readonly ResiliencePipeline _pipeline =
        new ResiliencePipelineBuilder().Build();

    [Fact]
    public void WithPolly_Pipeline_ReturnsResilientSecretClient()
    {
        var resilient = _client.WithPolly(_pipeline);

        Assert.NotNull(resilient);
        Assert.Same(_client, resilient.Inner);
    }

    [Fact]
    public void WithPolly_Configure_ReturnsResilientSecretClient()
    {
        var resilient = _client.WithPolly(p => p.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            ShouldHandle = KeyVaultTransientErrors.IsTransient,
        }));

        Assert.NotNull(resilient);
        Assert.Same(_client, resilient.Inner);
    }

    [Fact]
    public void WithPolly_InnerIsOriginalClient()
    {
        var resilient = _client.WithPolly(_pipeline);

        Assert.Same(_client, resilient.Inner);
    }
}
