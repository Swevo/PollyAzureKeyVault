public class KeyVaultTransientErrorsTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(504)]
    public void StatusCodes_ContainsTransientStatusCode(int statusCode)
    {
        Assert.Contains(statusCode, KeyVaultTransientErrors.StatusCodes);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public void StatusCodes_DoesNotContainNonTransientStatusCode(int statusCode)
    {
        Assert.DoesNotContain(statusCode, KeyVaultTransientErrors.StatusCodes);
    }

    [Fact]
    public void StatusCodes_HasThreeEntries()
    {
        Assert.Equal(3, KeyVaultTransientErrors.StatusCodes.Count);
    }

    [Fact]
    public void IsTransient_IsNotNull()
    {
        Assert.NotNull(KeyVaultTransientErrors.IsTransient);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task IsTransient_HandlesRequestFailedExceptionForTransientStatus(int statusCode)
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                ShouldHandle = KeyVaultTransientErrors.IsTransient,
            })
            .Build();

        var attempts = 0;
        await Assert.ThrowsAsync<RequestFailedException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                attempts++;
                throw new RequestFailedException(statusCode, "transient");
            }).AsTask());

        Assert.Equal(2, attempts); // original + 1 retry
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task IsTransient_DoesNotRetryNonTransientRequestFailedException(int statusCode)
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                ShouldHandle = KeyVaultTransientErrors.IsTransient,
            })
            .Build();

        var attempts = 0;
        await Assert.ThrowsAsync<RequestFailedException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                attempts++;
                throw new RequestFailedException(statusCode, "non-transient");
            }).AsTask());

        Assert.Equal(1, attempts); // no retry
    }

    [Fact]
    public async Task IsTransient_HandlesHttpRequestException()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                ShouldHandle = KeyVaultTransientErrors.IsTransient,
            })
            .Build();

        var attempts = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                attempts++;
                throw new HttpRequestException("network error");
            }).AsTask());

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task IsTransient_HandlesTaskCanceledException()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                ShouldHandle = KeyVaultTransientErrors.IsTransient,
            })
            .Build();

        var attempts = 0;
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                attempts++;
                throw new TaskCanceledException("timed out");
            }).AsTask());

        Assert.Equal(2, attempts);
    }
}
