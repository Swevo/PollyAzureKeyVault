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

## Related Packages

| Package | Downloads | Description |
|---|---|---|
| [PollyHealthChecks](https://www.nuget.org/packages/PollyHealthChecks) | [![Downloads](https://img.shields.io/nuget/dt/PollyHealthChecks.svg)](https://www.nuget.org/packages/PollyHealthChecks) | ASP.NET Core health checks for Polly v8 circuit breakers — expose circuit-breaker state (Closed, HalfOpen, Open, Isolated) as /health endpoint responses |
| [PollyBackoff](https://www.nuget.org/packages/PollyBackoff) | [![Downloads](https://img.shields.io/nuget/dt/PollyBackoff.svg)](https://www.nuget.org/packages/PollyBackoff) | Backoff delay strategies for Polly v8 resilience pipelines |
| [PollyGrpc](https://www.nuget.org/packages/PollyGrpc) | [![Downloads](https://img.shields.io/nuget/dt/PollyGrpc.svg)](https://www.nuget.org/packages/PollyGrpc) | Polly v8 resilience interceptor for gRPC |
| [PollyEFCore](https://www.nuget.org/packages/PollyEFCore) | [![Downloads](https://img.shields.io/nuget/dt/PollyEFCore.svg)](https://www.nuget.org/packages/PollyEFCore) | Polly v8 resilience pipelines for Entity Framework Core — wrap every EF Core query and SaveChanges with retry, timeout and circuit-breaker via a single AddPollyResilience() call |
| [PollyRabbitMQ](https://www.nuget.org/packages/PollyRabbitMQ) | [![Downloads](https://img.shields.io/nuget/dt/PollyRabbitMQ.svg)](https://www.nuget.org/packages/PollyRabbitMQ) | Polly v8 resilience for RabbitMQ.Client v7+ — retry, circuit-breaker, and timeout for IChannel operations, with built-in RabbitMqTransientErrors predicate covering AlreadyClosedException, BrokerUnreachableException, OperationInterruptedException, and ConnectFailureException |
| [PollyMailKit](https://www.nuget.org/packages/PollyMailKit) | [![Downloads](https://img.shields.io/nuget/dt/PollyMailKit.svg)](https://www.nuget.org/packages/PollyMailKit) | Polly v8 resilience pipelines for MailKit — retry, timeout, and circuit-breaker for SmtpClient.SendAsync and any MailKit SMTP operation |
| [PollyMassTransit](https://www.nuget.org/packages/PollyMassTransit) | [![Downloads](https://img.shields.io/nuget/dt/PollyMassTransit.svg)](https://www.nuget.org/packages/PollyMassTransit) | Polly v8 resilience pipelines for MassTransit — retry, timeout, and circuit-breaker for IBus.Publish and ISendEndpointProvider.Send |
| [PollyNpgsql](https://www.nuget.org/packages/PollyNpgsql) | [![Downloads](https://img.shields.io/nuget/dt/PollyNpgsql.svg)](https://www.nuget.org/packages/PollyNpgsql) | Polly v8 resilience pipelines for Npgsql (PostgreSQL) — retry, timeout, and circuit-breaker for NpgsqlConnection queries and commands, plus a built-in PostgresTransientErrors predicate covering all common PostgreSQL transient SQLSTATE codes |
| [PollyOpenAI](https://www.nuget.org/packages/PollyOpenAI) | [![Downloads](https://img.shields.io/nuget/dt/PollyOpenAI.svg)](https://www.nuget.org/packages/PollyOpenAI) | Polly v8 resilience for OpenAI and Azure OpenAI API calls |
| [PollyAzureEventHub](https://www.nuget.org/packages/PollyAzureEventHub) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureEventHub.svg)](https://www.nuget.org/packages/PollyAzureEventHub) | Polly v8 resilience pipelines for Azure Event Hubs — retry, timeout, and circuit-breaker for EventHubProducerClient and EventHubConsumerClient |
| [PollySignalR](https://www.nuget.org/packages/PollySignalR) | [![Downloads](https://img.shields.io/nuget/dt/PollySignalR.svg)](https://www.nuget.org/packages/PollySignalR) | Polly v8 reconnect policy for SignalR |
| [PollyElasticsearch](https://www.nuget.org/packages/PollyElasticsearch) | [![Downloads](https://img.shields.io/nuget/dt/PollyElasticsearch.svg)](https://www.nuget.org/packages/PollyElasticsearch) | Polly v8 resilience pipelines for Elastic.Clients.Elasticsearch 8+ — retry, timeout, and circuit-breaker for any Elasticsearch operation, plus a built-in ElasticTransientErrors predicate covering rate limiting (429), service unavailability (503), gateway timeouts (504), and connection failures |
| [PollyHangfire](https://www.nuget.org/packages/PollyHangfire) | [![Downloads](https://img.shields.io/nuget/dt/PollyHangfire.svg)](https://www.nuget.org/packages/PollyHangfire) | Polly v8 resilience pipelines for Hangfire — retry, timeout, and circuit-breaker for IBackgroundJobClient.Enqueue and Schedule |
| [PollyCosmosDb](https://www.nuget.org/packages/PollyCosmosDb) | [![Downloads](https://img.shields.io/nuget/dt/PollyCosmosDb.svg)](https://www.nuget.org/packages/PollyCosmosDb) | Polly v8 resilience pipelines for Azure Cosmos DB — retry, timeout, and circuit-breaker for Container operations, plus a built-in CosmosTransientErrors predicate covering rate limiting (429), timeouts (408), partition failovers (410), and service unavailability (503) |
| [PollySendGrid](https://www.nuget.org/packages/PollySendGrid) | [![Downloads](https://img.shields.io/nuget/dt/PollySendGrid.svg)](https://www.nuget.org/packages/PollySendGrid) | Polly v8 resilience pipelines for SendGrid — retry, timeout, and circuit-breaker for ISendGridClient.SendEmailAsync |
| [PollyMongo](https://www.nuget.org/packages/PollyMongo) | [![Downloads](https://img.shields.io/nuget/dt/PollyMongo.svg)](https://www.nuget.org/packages/PollyMongo) | Polly v8 resilience pipelines for MongoDB.Driver — wrap Find, InsertOne, UpdateOne, DeleteOne and other IMongoCollection calls with retry, timeout, circuit-breaker, and more using a single ResilientMongoCollection decorator |
| [PollyDapper](https://www.nuget.org/packages/PollyDapper) | [![Downloads](https://img.shields.io/nuget/dt/PollyDapper.svg)](https://www.nuget.org/packages/PollyDapper) | Polly v8 resilience pipelines for Dapper — wrap QueryAsync, ExecuteAsync, and other Dapper calls with retry, timeout, circuit-breaker, and more using a single ResilientDbConnection decorator |
| [PollyMediatR](https://www.nuget.org/packages/PollyMediatR) | [![Downloads](https://img.shields.io/nuget/dt/PollyMediatR.svg)](https://www.nuget.org/packages/PollyMediatR) | Polly v8 resilience pipelines for MediatR — add retry, timeout, circuit-breaker, rate-limiting, hedging, and chaos engineering to any MediatR request handler with a single line of DI registration |
| [PollySqlClient](https://www.nuget.org/packages/PollySqlClient) | [![Downloads](https://img.shields.io/nuget/dt/PollySqlClient.svg)](https://www.nuget.org/packages/PollySqlClient) | Polly v8 resilience pipelines for Microsoft.Data.SqlClient (SQL Server and Azure SQL) — retry, timeout, and circuit-breaker for SqlConnection queries and commands, plus a built-in SqlServerTransientErrors predicate covering all common SQL Server and Azure SQL transient error numbers |
| [PollyAzureQueueStorage](https://www.nuget.org/packages/PollyAzureQueueStorage) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureQueueStorage.svg)](https://www.nuget.org/packages/PollyAzureQueueStorage) | Polly v8 resilience pipelines for Azure Queue Storage — retry, timeout, and circuit-breaker for Azure.Storage.Queues QueueClient |
| [PollyRedis](https://www.nuget.org/packages/PollyRedis) | [![Downloads](https://img.shields.io/nuget/dt/PollyRedis.svg)](https://www.nuget.org/packages/PollyRedis) | Polly v8 resilience for StackExchange.Redis |
| [PollyAzureServiceBus](https://www.nuget.org/packages/PollyAzureServiceBus) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureServiceBus.svg)](https://www.nuget.org/packages/PollyAzureServiceBus) | Polly v8 resilience for Azure Service Bus — retry, circuit breaker, and timeout for sending and receiving messages |
| [PollyAzureBlob](https://www.nuget.org/packages/PollyAzureBlob) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureBlob.svg)](https://www.nuget.org/packages/PollyAzureBlob) | Polly v8 resilience pipelines for Azure Blob Storage — wrap BlobClient and BlobContainerClient operations with retry, timeout, circuit-breaker, and more using ResilientBlobClient and ResilientBlobContainerClient decorators |
| [PollyKafka](https://www.nuget.org/packages/PollyKafka) | [![Downloads](https://img.shields.io/nuget/dt/PollyKafka.svg)](https://www.nuget.org/packages/PollyKafka) | Polly v8 resilience for Confluent.Kafka — retry, circuit breaker, and timeout for producers and consumers |
| [PollyAzureTableStorage](https://www.nuget.org/packages/PollyAzureTableStorage) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureTableStorage.svg)](https://www.nuget.org/packages/PollyAzureTableStorage) | Polly v8 resilience pipelines for Azure Table Storage — retry, timeout, and circuit-breaker for Azure.Data.Tables TableClient |

## 💼 Need .NET consulting?

The author of this package is available for consulting on **Polly v8 resilience**, **Azure cloud architecture**, and **clean .NET design**.

**[→ solidqualitysolutions.com](https://www.solidqualitysolutions.com/)** · **[LinkedIn](https://www.linkedin.com/in/justbannister/)**
## License

MIT © [Justin Bannister](https://github.com/Swevo)