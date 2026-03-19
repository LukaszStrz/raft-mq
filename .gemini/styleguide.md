# raft-mq Style Guide

## Architecture
- **Hexagonal Architecture (Ports & Adapters)**: `RaftMq.Core` must have ZERO dependencies on external infrastructure, databases, MQ, or File System. All external interactions must go through interfaces in `RaftMq.Core.Interfaces`.
- **Adapters**: Implementations go in separate projects (e.g., `RaftMq.Transport.RabbitMq`, `RaftMq.Infrastructure.File`).

## C# Standards
- **Target Framework**: .NET Standard 2.1 for Core, .NET 10.0 for tests/apps.
- **C# Version**: C# 10.0+ features are mandatory.
- **Namespaces**: Use file-scoped namespaces (e.g., `namespace RaftMq.Core;`).
- **Nullability**: `<Nullable>enable</Nullable>` and `<WarningsAsErrors>nullable</WarningsAsErrors>` must be used in all projects.

## Asynchronous Programming
- Strictly follow the Task-based Asynchronous Pattern (TAP).
- **NEVER** use `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()`.
- **Always** use `ConfigureAwait(false)` in library code to prevent deadlocks.
- Accept `CancellationToken` in all public async methods.

## Testing Guidelines
- Use **xUnit** as the testing framework.
- Use **NSubstitute** for mocking interfaces.
- Use **FluentAssertions** for all assertions (`.Should().Be()`).
- Use **AutoFixture** to generate anonymous test data.
- Use **Testcontainers** for integration tests requiring RabbitMQ.

## Code Style & Convention
- Command pattern: All state changes must be an `IRaftCommand` serializable via `System.Text.Json`.
- Constructor injection must be used for all dependencies. No Service Locator pattern.
- Private fields must be prefixed with an underscore (`_field`).
- Use structured logging via `ILogger<T>`. 
