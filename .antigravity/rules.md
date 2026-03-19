# Antigravity Rules: raft-mq

## 1. Language & Framework Standards
- **Target Framework:** .NET Standard 2.1.
- **C# Version:** C# 10.0 or higher.
- **Namespaces:** Use file-scoped namespaces (e.g., `namespace RaftMq.Core;`) to reduce nesting.
- **Asynchrony:** - Strictly follow the Task-based Asynchronous Pattern (TAP).
    - NEVER use `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()`.
    - Always use `ConfigureAwait(false)` in library code to avoid deadlock scenarios in different sync contexts.
    - Accept `CancellationToken` in all public async methods.

## 2. Architectural Integrity
- **Hexagonal Architecture:** - The `RaftMq.Core` project must have ZERO dependencies on external infrastructure (No RabbitMQ, no File System).
    - All external interactions must happen through interfaces defined in `RaftMq.Core.Interfaces`.
- **Command Pattern:** - All state changes MUST be encapsulated in a class implementing `IRaftCommand`.
    - Commands must be serializable via `System.Text.Json`.

## 3. Coding Style
- **Naming:** - Interfaces must start with `I`.
    - Private fields must be prefixed with an underscore (e.g., `_logger`).
    - Use meaningful, descriptive names for Raft-specific variables (e.g., `currentTerm` vs `ct`).
- **Dependency Injection:** Use constructor injection for all dependencies. Avoid the Service Locator pattern.

## 4. Testing & Reliability
- **Unit Tests:** Every core logic class must have a corresponding test class in `RaftMq.Tests.Unit`.
- **Mocks:** Use NSubstitute for interface mocking in unit tests.
- **Assertions:** Use FluentAssertions for assertions in unit tests.
- **Test data:** Use AutoFixture for test data creation in unit tests.
- **Logging:** Inject `ILogger<T>` into all services. Use structured logging; do not use string interpolation in log messages (e.g., `_logger.LogInformation("Node {NodeId} became Leader", id);`).

## 5. Raft Specifics
- **Safety First:** When implementing log operations, always ensure the Write-Ahead Log (WAL) principle is followed: Persist to the `IPersistenceProvider` BEFORE applying to the `IStateMachine`.