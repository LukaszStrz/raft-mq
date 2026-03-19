# Implementation Plan: raft-mq

## Phase 1: Foundations & Models
- [x] **Project Setup**: Create `RaftMq.Core` and `RaftMq.Tests.Unit`.
- [x] **Command Pattern**: Define `IRaftCommand` and JSON serialization logic for polymorphic command types.
- [x] **Internal Models**: Define `LogEntry`, `AppendEntriesRequest/Response`, and `RequestVoteRequest/Response`.
- [x] **State Machine Port**: Define `IStateMachine` with `ApplyAsync(IRaftCommand)`.

## Phase 2: The Core Raft Engine
- [x] **Persistence Port**: Define `IPersistenceProvider` and implement a `FilePersistenceProvider` as the MVP reference.
- [x] **Node State Manager**: Implement the `RaftNode` class to manage transitions between `Follower`, `Candidate`, and `Leader`.
- [x] **Election Logic**: Implement randomized election timers and the `RequestVote` handler.
- [x] **Replication Logic**: Implement the `AppendEntries` handler and the log commitment logic.

## Phase 3: Transport & Integration
- [x] **Transport Port**: Define `ITransport`.
- [x] **RabbitMQ Adapter**: Create `RaftMq.Transport.RabbitMq` project. Implement the producer/consumer logic for RPCs.
- [x] **Integration Test Suite**: Setup `Testcontainers` to spin up RabbitMQ and run a multi-node replication test.

## Phase 4: Hardening
- [x] **Error Handling**: Implement MQ-specific retry logic and connection recovery.
- [x] **Logging**: Integrate `Microsoft.Extensions.Logging` throughout the core logic for traceability.

## Phase 5: Console Demonstrator
- [x] **Scaffolding & Domain**: Create `RaftMq.Demo.Console` with `KeyValueCommand` and `KeyValueStateMachine`.
- [x] **Interactive Terminal**: Implement a dynamic host loop to observe cluster elections and issue distributed payloads.

## Phase 6: Autodiscovery & Dynamic Membership
- [x] **Transport Discovery**: Implement a `Fanout` exchange in `RabbitMqTransportProvider` for broadcasting and receiving `NodeAnnounced` events.
- [x] **Joint Consensus**: Define `AddNodeCommand` and implement dynamic cluster configuration changes so `RaftNode` can update its `majority` quorum safely.
- [x] **Demonstrator Update**: Update the Console App to allow launching `dotnet run <any_random_id>` and watch it dynamically join the living cluster.