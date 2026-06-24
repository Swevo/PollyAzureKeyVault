# PollyAzureKeyVault

[![NuGet](https://img.shields.io/nuget/v/PollyAzureKeyVault.svg)](https://www.nuget.org/packages/PollyAzureKeyVault/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PollyAzureKeyVault.svg)](https://www.nuget.org/packages/PollyAzureKeyVault/)
[![CI](https://github.com/Swevo/PollyAzureKeyVault/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/PollyAzureKeyVault/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Polly v8 resilience for `Azure.Security.KeyVault.Secrets`** — add retry, timeout, and circuit-breaker to any Azure Key Vault operation in two lines.

```csharp
var secretClient = new SecretClient(new Uri("https://my-vault.vault.azure.net/"), credential);

var resilient = secretClient.WithPolly(pipeline => pipeline
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(2),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = KeyVaultTransientErrors.IsTransient,
    })
    .AddTimeout(TimeSpan.FromSeconds(10)));

var secret = await resilient.GetSecretAsync("my-secret");
```

## Why PollyAzureKeyVault?

Azure Key Vault is a hard dependency for most production apps — connection failures at startup or runtime cause outages. This library adds Polly v8 resilience without boilerplate:

| Problem | Solution |
|---------|----------|
| HTTP 429 throttling (Key Vault has strict rate limits) | Caught by `KeyVaultTransientErrors.IsTransient` |
| HTTP 503 during Key Vault maintenance / regional failover | Caught by `KeyVaultTransientErrors.IsTransient` |
| HTTP 504 gateway timeout | Caught by `KeyVaultTransientErrors.IsTransient` |
| `HttpRequestException` network failure | Caught by `KeyVaultTransientErrors.IsTransient` |
| `TaskCanceledException` slow response | Caught by `KeyVaultTransientErrors.IsTransient` |
| Cascading failures propagating to the rest of the app | Wrap with `AddCircuitBreaker` |

## Installation

```
dotnet add package PollyAzureKeyVault
dotnet add package Polly.Core
```

## Quick-start

### 1. Manual wiring

```csharp
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Polly;
using Polly.Retry;

var credential = new DefaultAzureCredential();
var client = new SecretClient(new Uri("https://my-vault.vault.azure.net/"), credential);

var resilient = client.WithPolly(p => p
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(2),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = KeyVaultTransientErrors.IsTransient,
    }));

// Typed convenience methods
var secret = await resilient.GetSecretAsync("connection-string");
Console.WriteLine(secret.Value.Value);
```

### 2. Dependency injection

```csharp
// Program.cs / Startup.cs
builder.Services.AddSingleton(new SecretClient(
    new Uri("https://my-vault.vault.azure.net/"),
    new DefaultAzureCredential()));

builder.Services.AddPollyAzureKeyVault(pipeline => pipeline
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(2),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = KeyVaultTransientErrors.IsTransient,
    })
    .AddTimeout(TimeSpan.FromSeconds(10)));

// Inject ResilientSecretClient into your services
public class SecretsService(ResilientSecretClient client)
{
    public async Task<string> GetConnectionStringAsync(CancellationToken ct) =>
        (await client.GetSecretAsync("db-connection-string", cancellationToken: ct)).Value.Value;
}
```

### 3. With a URI shortcut (registers SecretClient automatically)

```csharp
builder.Services.AddPollyAzureKeyVault(
    new Uri("https://my-vault.vault.azure.net/"),
    pipeline => pipeline
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            ShouldHandle = KeyVaultTransientErrors.IsTransient,
        }));
```

## Transient error reference

```csharp
// Use in any Polly strategy:
ShouldHandle = KeyVaultTransientErrors.IsTransient
```

| Condition | Why it's transient |
|-----------|-------------------|
| `RequestFailedException` (HTTP 429) | Key Vault rate limit — back off and retry |
| `RequestFailedException` (HTTP 503) | Vault maintenance or regional failover |
| `RequestFailedException` (HTTP 504) | Proxy or load balancer timed out |
| `HttpRequestException` | Network failure |
| `TaskCanceledException` | Request timed out in transit |

### Handling only 429s with a longer back-off

```csharp
.AddRetry(new RetryStrategyOptions
{
    ShouldHandle = new PredicateBuilder()
        .Handle<RequestFailedException>(ex => ex.Status == 429),
    MaxRetryAttempts = 5,
    Delay = TimeSpan.FromSeconds(10),
    BackoffType = DelayBackoffType.Exponential,
})
```

## Advanced pipelines

### Full production pipeline

```csharp
client.WithPolly(p => p
    .AddTimeout(TimeSpan.FromSeconds(30))
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 4,
        Delay = TimeSpan.FromSeconds(2),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = KeyVaultTransientErrors.IsTransient,
        OnRetry = args =>
        {
            logger.LogWarning("Key Vault retry {Attempt} — {Exception}",
                args.AttemptNumber, args.Outcome.Exception?.Message);
            return ValueTask.CompletedTask;
        },
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30),
        ShouldHandle = KeyVaultTransientErrors.IsTransient,
    }));
```

### Arbitrary operations via `ExecuteAsync`

```csharp
// Use ExecuteAsync<T> for any SecretClient method not covered by typed overloads
var props = await resilient.ExecuteAsync(
    (c, ct) => c.GetDeletedSecretAsync("old-secret", ct));
```

## API reference

### `ResilientSecretClient`

| Member | Description |
|--------|-------------|
| `Inner` | The underlying `SecretClient` |
| `GetSecretAsync(name, version?, ct)` | Retrieves a secret through the pipeline |
| `SetSecretAsync(name, value, ct)` | Creates or updates a secret through the pipeline |
| `StartDeleteSecretAsync(name, ct)` | Begins deletion through the pipeline |
| `ExecuteAsync<T>(operation, ct)` | Runs any `SecretClient` operation through the pipeline |

### `KeyVaultTransientErrors`

| Member | Description |
|--------|-------------|
| `IsTransient` | `PredicateBuilder` for 429/503/504 `RequestFailedException`, `HttpRequestException`, `TaskCanceledException` |
| `StatusCodes` | `IReadOnlySet<int>` — `{429, 503, 504}` |

### Extension methods

| Method | Description |
|--------|-------------|
| `client.WithPolly(pipeline)` | Wraps a `SecretClient` with a pre-built `ResiliencePipeline` |
| `client.WithPolly(configure)` | Builds a pipeline inline and wraps the client |

### DI extensions

| Method | Description |
|--------|-------------|
| `services.AddPollyAzureKeyVault(configure)` | Registers `ResiliencePipeline` + `ResilientSecretClient` (requires `SecretClient` already in DI) |
| `services.AddPollyAzureKeyVault(uri, configure)` | Registers `SecretClient` with `DefaultAzureCredential`, then pipeline + resilient client |

## Target frameworks

| Framework | Supported |
|-----------|-----------|
| .NET 6 | ✅ |
| .NET 8 | ✅ |
| .NET 9 | ✅ |

## Related packages

| Package | Description |
|---------|-------------|
| [PollyAzureBlob](https://github.com/Swevo/PollyAzureBlob) | Polly v8 for Azure Blob Storage |
| [PollyAzureServiceBus](https://github.com/Swevo/PollyAzureServiceBus) | Polly v8 for Azure Service Bus |
| [PollyCosmosDb](https://github.com/Swevo/PollyCosmosDb) | Polly v8 for Azure Cosmos DB |
| [PollyElasticsearch](https://github.com/Swevo/PollyElasticsearch) | Polly v8 for Elastic.Clients.Elasticsearch |
| [PollyRedis](https://github.com/Swevo/PollyRedis) | Polly v8 for StackExchange.Redis |
| [PollyEFCore](https://github.com/Swevo/PollyEFCore) | Polly v8 for Entity Framework Core |
| [PollyDapper](https://github.com/Swevo/PollyDapper) | Polly v8 for Dapper |
| [PollyMongo](https://github.com/Swevo/PollyMongo) | Polly v8 for MongoDB |
| [PollyNpgsql](https://github.com/Swevo/PollyNpgsql) | Polly v8 for Npgsql (PostgreSQL) |
| [PollySqlClient](https://github.com/Swevo/PollySqlClient) | Polly v8 for Microsoft.Data.SqlClient |
| [PollyGrpc](https://github.com/Swevo/PollyGrpc) | Polly v8 for gRPC |
| [PollyRabbitMQ](https://github.com/Swevo/PollyRabbitMQ) | Polly v8 for RabbitMQ |
| [PollyKafka](https://github.com/Swevo/PollyKafka) | Polly v8 for Confluent.Kafka |
| [PollySignalR](https://github.com/Swevo/PollySignalR) | Polly v8 for SignalR |
| [PollyOpenAI](https://github.com/Swevo/PollyOpenAI) | Polly v8 for OpenAI .NET SDK |
| [PollyMediatR](https://github.com/Swevo/PollyMediatR) | Polly v8 for MediatR |
| [PollyHealthChecks](https://github.com/Swevo/PollyHealthChecks) | Polly v8 for ASP.NET Core Health Checks |
| [PollyBackoff](https://github.com/Swevo/PollyBackoff) | Polly v8 backoff helpers |

## License

MIT © [Justin Bannister](https://github.com/Swevo)