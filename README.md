# Raft-MQ

A robust, asynchronous, and dynamically scaling implementation of the **Raft Consensus Algorithm** natively built in C# (.NET Standard 2.1 / .NET 10). 

Unlike traditional Raft distributions that rely on direct HTTP or gRPC point-to-point networks, `Raft-MQ` leverages **RabbitMQ** as its foundational transport layer. This enables powerful decoupled messaging, durable RPC routing, and native fanout-based node autodiscovery while mathematically preserving the safety constraints of the original distributed consensus paper.

---

## 🌟 Key Features

* **Strict Raft Protocol**: Implements Leader Election, Log Replication, Term Validation, Heartbeats, and generalized State Machine assertions.
* **Message Queue Transport**: Fully decoupled RPC communication utilizing RabbitMQ Direct channels and pseudo-synchronous RPC queues.
* **Autodiscovery (Joint Consensus)**: Nodes seamlessly join living clusters asynchronously! By utilizing a RabbitMQ `Fanout` exchange, new instances broadcast their presence. The active cluster Leader organically transitions the cluster capacity across the log using Raft Dynamic Membership rules, scaling the active quorum size entirely automatically.
* **Pluggable Persistence**: Persists log collections, current terms, and voter footprints to disk to survive aggressive node failures.
* **Task-based Asynchronous Pattern (TAP)**: Non-blocking, rigorously architected asynchronous data streams with strict multi-threading concurrency controls.

## 🏗️ Architecture Layout

This monolithic repository follows strict Hexagonal Architecture constraints:

- **`RaftMq.Core`**: The beating heart of the infrastructure. Houses the `RaftNode<T>`, `IRaftCommand`, generic constraints, and Election state processors. Entirely agnostic.
- **`RaftMq.Infrastructure.File`**: Exposes the standard `FilePersistenceProvider` for JSON-serialized local-disk persistence tracking.
- **`RaftMq.Transport.RabbitMq`**: Adapts standard RabbitMQ configurations into the generic `ITransport<T>` logic layer, bridging Fanout discovery paradigms and point-to-point delivery.
- **`RaftMq.Demo.Console`**: A fully decoupled interactive playground hosting a local distributed `KeyValueStateMachine`.

---

## 🚀 Getting Started (Demonstrator)

The repository provides a built-in sandbox allowing developers to watch the algorithm self-heal live!

### 1. Deploy the RabbitMQ Broker
Ensure Docker Daemon is active, then spin up the foundational network broker provided in the root directory:
```bash
docker compose up -d
```
*(Broker activates at `localhost:5672`, Management UI is mapped to `http://localhost:15672`)*

### 2. Launching Seed Nodes
Open **three independent terminals**. In each shell, power up a distinct node to bootstrap the initial election quorum:

```bash
# Terminal 1
dotnet run --project RaftMq.Demo.Console/RaftMq.Demo.Console.csproj nodeA

# Terminal 2
dotnet run --project RaftMq.Demo.Console/RaftMq.Demo.Console.csproj nodeB

# Terminal 3
dotnet run --project RaftMq.Demo.Console/RaftMq.Demo.Console.csproj nodeC
```
*Wait a few seconds for the initial leader to organically emerge from the timeouts.*

### 3. Dynamic Node Inclusion (Autodiscovery)
Because `Raft-MQ` leverages a dynamically mapped Joint Consensus structure, you may arbitrarily scale the cluster outward without configurations. Open a fresh terminal and run:
```bash
# Terminal 4
dotnet run --project RaftMq.Demo.Console/RaftMq.Demo.Console.csproj myDynamicNode
```
*You will immediately see the isolated node broadcast its arrival... The Leader will intercept the event, publish an `AddNodeCommand` exclusively into the Raft log, and as soon as the cluster replicates the transaction—the cluster scales its voting capacity and acknowledges the new actor automatically!*

### 4. Injecting Logs Iteratively
You can interact with the environment through standard input within any shell:
- `status`: Renders internal cluster tracking IDs, Election Terms, and current `Follower`/`Candidate`/`Leader` states.
- `set <key> <value>`: Transmits a distributed `KeyValueCommand` into the active Leader. Observe the payload as it propagates across the RabbitMQ transport layers to all recognized Follower terminals!

### 5. Simulate Server Outages
To verify durability, feel free to **Ctrl+C** any actively participating terminal (even the Leader!). You will witness the remaining ecosystem independently trigger a secondary election and restore communication functionality without manual interference.