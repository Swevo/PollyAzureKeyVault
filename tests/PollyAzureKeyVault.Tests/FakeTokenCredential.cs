using Azure.Core;

/// <summary>Minimal token credential that never makes network calls — for unit tests only.</summary>
internal sealed class FakeTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1)));
}
