# Azure Performance Analysis

This repository contains a number of services used to measure the latency and throughput of various Azure services.

## Latency Performance Analysis

Latency performance analysis finds the optimal (lowest) latency by using a low throughput write-only workload with no write contention (10 writes/sec, ~1 KB).  Azure service settings are adjusted to attempt to get as close a comparison as possible across services.  Most services are configured to be strongly consistent with replication and persistence.

| Azure Service   | Avg Latency (ms) | P99 Latency (ms) | Notes |
| --------------- | :--------------: | :--------------: | ----- |
| DocumentDB      |       6.5        |        35        | strong consistency, no indexing, 1 region, 400 RUs, TTL = 1 day |
| Event Hub       |       36         |       320        | 2 partitions, 10 throughput units, 1 day message retention |
| Redis           |       1.1        |       3.1        | C2 Standard (async replication, no data persistence, dedicated service, moderate network bandwidth) |
| SQL             |       7.8        |        60        | S2 Standard (50 DTUs), 1 region, insert-only writes |
| Storage (Blob)  |       37         |       240        | Standard, Locally-redundant storage (LRS) |
| Storage (Table) |       50         |       350        | Standard, Locally-redundant storage (LRS) |
| ServiceFabric Queue |  3.2         |         9        | IReliableConcurrentQueue, D2v2 (SSD), 3 replicas, POD class with DataContract serialization, BatchAcknowledgementInterval = 0ms, writes measured from the stateful service |
| ServiceFabric Dictionary |   3.2   |        10        | IReliableDictionary, D2v2 (SSD), 3 replicas, key = string, value = POD class with DataContract serialization, BatchAcknowledgementInterval = 0ms, writes measured from the stateful service |
| ServiceFabric Dictionary + ServiceProxy | 4.2 |  14   | IReliableDictionary, D2v2 (SSD), 3 replicas, key = string, value = POD class with DataContract serialization, BatchAcknowledgementInterval = 0ms, writes measured from a stateless service communicating via ServiceProxy to the stateful service |
| ServiceFabric Stateful Actor | 3.9 |        17        | StatefulActor, StatePersistence = Persisted, D2v2 (SSD), 3 replicas, POD class with DataContract serialization, BatchAcknowledgementInterval = 0ms, writes measured from a stateless service communicating via ActorProxy to the stateful actor |
| ServiceFabric Volatile Actor | 3.9 |         9        | StatefulActor, StatePersistence = Volatile, D2v2 (SSD), 3 replicas, POD class with DataContract serialization, StatePersistence = Volatile, BatchAcknowledgementInterval = 0ms, writes measured from a stateless service communicating via ActorProxy to the volatile actor |

## Throughput Performance Analysis

Throughput performance analysis finds the optimal throughput numbers by using a high throughput write-only workload with no write contention (~1 KB writes).  Azure service settings are adjusted to attempt to get as close a comparison as possible across services.  Most services are configured to be strongly consistent with replication and persistence.  The Azure services are sized to provide an approximately equivalent operating cost.  Cost shown is based on public Azure pricing for South Central US as calculated by https://azure.microsoft.com/en-us/pricing/calculator.

| Azure Service   | Avg Throughput (writes/sec) | P99 Throughput (writes/sec) | Avg Latency (ms) | Cost / month | Notes |
| --------------- | :-------------------------: | :-------------------------: | :--------------: | :----------: | ----- |
| DocumentDB      |              1,500          |            2,100            |       20.5       | $876 | strong consistency, no indexing, 1 region, 15000 RUs, TTL = 1 day |
| Event Hub       |             15,000          |           31,000            |        7.0       | ~$1,059* | 32 partitions, 10 throughput units, 1 day message retention |
| Redis           |            115,000          |          125,000            |        9.1       | $810 | P2 Premium (async replication, no data persistence, dedicated service, redis cluster, moderate network bandwidth) |
| SQL             |             11,400          |           12,800            |        2.9       | $913 | P2 Premium (250 DTUs), 1 region, insert-only writes |
| ServiceFabric Queue |          8,600          |           10,500            |        3.7       | $924 | IReliableConcurrentQueue, D3v2 (SSD), 3 replicas, POD class with DataContract serialization, BatchAcknowledgementInterval = 15ms, MaxPrimaryReplicationQueueSize/MaxSecondaryReplicationQueueSize = 1M, writes measured from the stateful service |
| ServiceFabric Dictionary (Write-only) |  8,000 |          11,000            |        4.2       | $924 | IReliableDictionary, D3v2 (SSD), 3 replicas, key = long, value = POD class with DataContract serialization, BatchAcknowledgementInterval = 15ms, MaxPrimaryReplicationQueueSize/MaxSecondaryReplicationQueueSize = 1M, writes measured from the stateful service |
| ServiceFabric Dictionary + ServiceProxy | 7,800 |         11,000            |        4.3       | $924* | IReliableDictionary, D3v2 (SSD), 3 replicas, key = long, value = POD class with DataContract serialization, BatchAcknowledgementInterval = 15ms, MaxPrimaryReplicationQueueSize/MaxSecondaryReplicationQueueSize = 1M, writes measured from a stateless service communicating via ServiceProxy to the stateful service |
| ServiceFabric Dictionary (Read-only) | 175,000 |         260,000            |        1.7       | $924 | IReliableDictionary, D3v2 (SSD), 3 replicas, key = long, value = POD class with DataContract serialization, BatchAcknowledgementInterval = 15ms, MaxPrimaryReplicationQueueSize/MaxSecondaryReplicationQueueSize = 1M, reads measured from the stateful service |

*Note:  Average latency per operation at this throughput is given - however it's measured as: (*latency to write a batch*) / (*number of writes in the batch*).  So a batch write of 10 documents that took 50 ms would show an average latency of 5 ms.*

*Note:  Throughput is measured every 1 second.  Average throughput indicates the average of these 1 second throughput measurements. P99 throughput indicates the 99th percentile of these 1 second throughput measurements.*

*Note: Prices shown are for configurations that are as close as possible to compare.  Service Fabric scenarios do not necessarily require a separate client machine, while DocumentDB/Event Hub/Redis/SQL do.  However, one should normally use 4 replicas in Service Fabric for reliability/availability.  Given these considerations, I show the prices for just the managed service itself (not the client machine) and for just 3 replicas (3 VMs) for Service Fabric scenarios.*

*Note: Event Hub pricing is $219/mo for 10 throughput units plus $840/mo for 1B messages/day (approximate throughput at this load).*

*Note: ServiceFabric Dictionary + ServiceProxy has an extra VM for the client machine, but the price is still shown for 3 VMs.*
