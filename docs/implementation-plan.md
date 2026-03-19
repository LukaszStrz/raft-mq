# Implementation Plan: raft-mq

## Phase 1: Foundations & Models
- [ ] **Project Setup**: Create `RaftMq.Core` and `RaftMq.Tests.Unit`.
- [ ] **Command Pattern**: Define `IRaftCommand` and JSON serialization logic for polymorphic command types.
- [ ] **Internal Models**: Define `LogEntry`, `AppendEntriesRequest/Response`, and `RequestVoteRequest/Response`.
- [ ] **State Machine Port**: Define `IStateMachine` with `ApplyAsync(IRaftCommand)`.

## Phase 2: The Core Raft Engine
- [ ] **Persistence Port**: Define `IPersistenceProvider` and implement a `FilePersistenceProvider` as the MVP reference.
- [ ] **Node State Manager**: Implement the `RaftNode` class to manage transitions between `Follower`, `Candidate`, and `Leader`.
- [ ] **Election Logic**: Implement randomized election timers and the `RequestVote` handler.
- [ ] **Replication Logic**: Implement the `AppendEntries` handler and the log commitment logic.

## Phase 3: Transport & Integration
- [ ] **Transport Port**: Define `ITransport`.
- [ ] **RabbitMQ Adapter**: Create `RaftMq.Transport.RabbitMq` project. Implement the producer/consumer logic for RPCs.
- [ ] **Integration Test Suite**: Setup `Testcontainers` to spin up RabbitMQ and run a multi-node replication test.

## Phase 4: Hardening
- [ ] **Error Handling**: Implement MQ-specific retry logic and connection recovery.
- [ ] **Logging**: Integrate `Microsoft.Extensions.Logging` throughout the core logic for traceability.