# Project Name: raft-mq

## 1. Overview
`raft-mq` is a C# implementation of the Raft Consensus Algorithm (.NET Standard 2.1) designed to operate over Message Queue transports. It decouples the Raft safety logic from the network layer, allowing for resilient, distributed state machines that communicate via brokers like RabbitMQ.

## 2. Functional Requirements
- [ ] **Core Raft Logic**: Implementation of Leader Election, Log Replication, and Safety (as per the Raft Paper).
- [ ] **Command Pattern Integration**: All state changes must be encapsulated in `IRaftCommand` objects.
- [ ] **State Machine Hook**: An `IStateMachine` interface that executes committed commands.
- [ ] **Pluggable Persistence**: An `IPersistenceProvider` interface for stable storage of the Log and Metadata (Term, Vote).
- [ ] **MQ Transport**: An `ITransport` interface implemented initially for RabbitMQ.
- [ ] **Async Native**: Strict adherence to Task-based Asynchronous Pattern (TAP).
- [ ] **Membership**: Support for Joint Consensus for cluster configuration changes.

## 3. Technical Specification
- **Framework**: .NET Standard 2.1
- **Language**: C# 10.0+
- **Transport**: `RabbitMQ.Client`
- **Serialization**: `System.Text.Json` (Polymorphic for Commands)
- **Testing**: `xUnit`, `Moq`, `Testcontainers` (for RabbitMQ integration).

## 4. Architecture & State
The project uses a **Hexagonal (Ports & Adapters)** architecture:
- **Core**: Contains the `RaftNode` (State Machine Logic), `LogEntry<T>`, and RPC Models.
- **Ports**: Interfaces for `ITransport`, `IPersistenceProvider`, and `IStateMachine`.
- **Adapters**: Concrete implementations (`RabbitMqTransport`, `FilePersistence`).

## 5. Success Criteria (Definition of Done)
- [ ] 100% of Raft safety properties (Election Safety, Leader Append-Only, Log Matching) verified via unit tests.
- [ ] Integration test: 3-node cluster performs leader election and command replication over a live RabbitMQ broker.
- [ ] Zero blocking calls (no `.Result` or `.Wait()`).
- [ ] Public API surface is fully documented via XML comments.