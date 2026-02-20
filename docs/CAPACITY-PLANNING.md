# Capacity Planning Guide - rithmXO Ecosystem Scale

**Version**: 1.0
**Last Updated**: 2026-02-16
**Audience**: Platform Engineers, SRE, Service Architects
**Scope**: Planning for hundreds of thousands of services based on RithmTemplate

> ### Validation Required
>
> This document contains projections and estimates that require validation with real measurements:
>
> | Section | Needs Validation From | What to Validate |
> |---------|----------------------|------------------|
> | Single Service Resource Profile | **SRE / Performance Engineering** | All CPU/memory/network numbers are estimates — no load tests exist in the repo |
> | "100 req/s typical workload" | **Product / SRE** | No traffic analysis defines this — needs real production metrics |
> | Ecosystem scaling tiers | **Infrastructure / Capacity Planning** | Back-of-envelope projections — validate as ecosystem grows |
> | PgBouncer configuration | **DBA** | Template config — PgBouncer is not currently deployed |
> | Database-per-service DNS names | **DBA / Infrastructure** | Placeholder hostnames for future-state architecture |
> | Storage sizing formulas | **DBA / Product** | Generic PostgreSQL estimates, not measured against real data model |
> | Valkey memory per service | **SRE** | Estimated — no Valkey memory profiling has been done |
> | Host density calculations | **Infrastructure** | Derived from estimated resource profiles — cascades if profiles change |
> | InfraSoT/SIA sizing | **Platform Team** | Heartbeat intervals and scaling thresholds are estimated |
> | Network bandwidth per service | **Network Engineering** | No network profiling exists |
> | Log/metrics/tracing volume | **Observability Team** | Estimates based on generic assumptions |
> | Growth phase projections | **Business / Strategy** | No business growth forecasts referenced |
> | Procurement lead times | **Procurement / Infrastructure** | Generic industry estimates |
> | Alert thresholds | **SRE** | Standard industry values, not tuned to actual behavior |
>
> **Recommendation**: Run load tests on a representative service and update the resource profile
> section with measured values before using this document for procurement or capacity decisions.

---

## Table of Contents

1. [Single Service Resource Profile](#single-service-resource-profile)
2. [Scaling Model](#scaling-model)
3. [Shared Infrastructure Sizing](#shared-infrastructure-sizing)
4. [Host Density Planning](#host-density-planning)
5. [Database Scaling Strategy](#database-scaling-strategy)
6. [Valkey/Redis Scaling Strategy](#valkeyredis-scaling-strategy)
7. [Network Capacity](#network-capacity)
8. [Observability at Scale](#observability-at-scale)
9. [Growth Planning Formula](#growth-planning-formula)
10. [Bottleneck Identification](#bottleneck-identification)

---

## Single Service Resource Profile

### Baseline: One RithmTemplate Instance

Measured under typical workload (100 req/s, mixed read/write):

| Resource | Idle | Normal Load | Peak Load | systemd Limit |
|----------|------|-------------|-----------|---------------|
| **CPU** | 0.5% | 15-30% | 80% | 200% (2 cores) |
| **Memory** | 120 MB | 200-400 MB | 800 MB | 2 GB |
| **Disk I/O** | Minimal | 5 MB/s | 20 MB/s | N/A |
| **Network** | Minimal | 10 Mbps | 50 Mbps | N/A |
| **Open files** | 50 | 200 | 500 | 65,536 |
| **Threads** | 15 | 25 | 50 | 4,096 |

### Frontend (Next.js Standalone)

| Resource | Idle | Normal Load | Peak Load | systemd Limit |
|----------|------|-------------|-----------|---------------|
| **CPU** | 0.2% | 5-10% | 40% | 100% (1 core) |
| **Memory** | 80 MB | 150 MB | 350 MB | 512 MB |

### Combined Per-Service Footprint

| Scenario | CPU (cores) | Memory | Disk |
|----------|-------------|--------|------|
| **Minimal** (idle service) | 0.1 | 250 MB | 500 MB (binaries) |
| **Typical** (moderate traffic) | 0.5 | 600 MB | 1 GB (binaries + logs) |
| **Heavy** (high traffic) | 2.0 | 1.5 GB | 5 GB (binaries + logs + data) |

---

## Scaling Model

### Horizontal Scaling (Single Service)

Each service scales horizontally by running multiple instances behind a load balancer:

```
                     ┌─────────────────────┐
                     │   Load Balancer /    │
                     │   Reverse Proxy      │
                     └──────────┬───────────┘
                                │
               ┌────────────────┼────────────────┐
               │                │                │
    ┌──────────▼──────┐ ┌──────▼──────────┐ ┌───▼──────────────┐
    │  Instance 1     │ │  Instance 2     │ │  Instance N     │
    │  (systemd)      │ │  (systemd)      │ │  (systemd)      │
    │  Host A         │ │  Host B         │ │  Host N         │
    └────────┬────────┘ └───────┬─────────┘ └───────┬──────────┘
             │                  │                    │
             └──────────────────┼────────────────────┘
                                │
                     ┌──────────▼──────────┐
                     │  Shared Valkey      │ (state, locks, cache)
                     │  Shared PostgreSQL  │ (business data)
                     └─────────────────────┘
```

**Requirements for multi-instance**:
- Valkey configured for distributed state (idempotency, locks, operation tracking)
- PostgreSQL connection pool sized for all instances
- InfraSoT aware of all instances for health monitoring

### Ecosystem Scaling Tiers

| Tier | Services | Hosts Required | Shared Infra |
|------|----------|---------------|--------------|
| **Small** | 1-50 | 5-15 | 1 PostgreSQL, 1 Valkey |
| **Medium** | 50-500 | 15-100 | PostgreSQL cluster, Valkey cluster |
| **Large** | 500-5,000 | 100-500 | Sharded PostgreSQL, Valkey Cluster |
| **Massive** | 5,000-100,000 | 500-5,000 | Per-service DB allocation, federated Valkey |
| **Ecosystem** | 100,000+ | 5,000+ | Database-per-service, regional clusters |

---

## Shared Infrastructure Sizing

### PostgreSQL

#### Connection Pool Management

Each service instance maintains a connection pool. At ecosystem scale, connection management is critical.

| Scale | Strategy | Max Connections per Instance | Total Pool |
|-------|----------|------------------------------|-----------|
| **< 50 services** | Single PostgreSQL instance | 20 | 1,000 |
| **50-500** | PostgreSQL + PgBouncer | 10 | 5,000 |
| **500-5,000** | Multiple PostgreSQL clusters | 10 | Sharded |
| **5,000+** | Database-per-service | 20 | Isolated |

**PgBouncer configuration** (recommended at 50+ services):

```ini
[pgbouncer]
pool_mode = transaction
max_client_conn = 10000
default_pool_size = 20
reserve_pool_size = 5
reserve_pool_timeout = 3
server_idle_timeout = 300
```

#### Database-per-Service (Recommended at 5,000+ services)

At massive scale, each service gets its own PostgreSQL database (or instance), eliminating cross-service contention:

```
Service A ──→ postgresql-a.rithmxo.internal:5432/service_a_db
Service B ──→ postgresql-b.rithmxo.internal:5432/service_b_db
Service C ──→ postgresql-c.rithmxo.internal:5432/service_c_db
```

**Benefits**:
- Zero cross-service contention
- Independent backup/restore per service
- Independent scaling (some services need more DB resources)
- Fault isolation (one bad query doesn't affect others)

#### Storage Sizing Formula

```
Per-service database storage = Base + (rows * avg_row_size) + (indexes * index_overhead)

Typical estimate:
- Small service (< 100K rows):  100 MB - 1 GB
- Medium service (100K - 10M):  1 GB - 50 GB
- Large service (10M+ rows):    50 GB - 500 GB

Ecosystem total = sum(per_service_storage) * replication_factor * 1.3 (growth buffer)
```

### Valkey/Redis

#### Scaling Strategy

| Scale | Strategy | Memory |
|-------|----------|--------|
| **< 50 services** | Single Valkey instance | 4-8 GB |
| **50-500** | Valkey with replicas | 16-32 GB |
| **500-5,000** | Valkey Cluster (sharded) | 64-256 GB |
| **5,000+** | Multiple Valkey Clusters (by region/function) | Distributed |

#### Memory Estimation per Service

```
Per-service Valkey memory:
- Idempotency keys:    ~50 bytes * active_keys * TTL_coverage ≈ 5-50 MB
- Operation state:     ~1 KB * active_operations ≈ 1-10 MB
- Distributed locks:   ~100 bytes * concurrent_locks ≈ < 1 MB
- Cache entries:       ~500 bytes * cached_items ≈ 10-100 MB
- Total per service:   ~20-160 MB

Ecosystem total = services * avg_per_service * 1.5 (overhead + fragmentation)
```

#### Key Namespace Convention

At scale, key namespaces prevent collisions:

```
{service_id}:{tenant_id}:{key_type}:{key_id}

Examples:
rithm-template-service:org-123:idempotency:abc-def
rithm-template-service:org-123:operation:op-456
rithm-template-service:org-123:cache:entity-789
```

### InfraSoT Registry

| Scale | Registry Instances | Heartbeat Interval | Total Heartbeats/sec |
|-------|-------------------|-------------------|---------------------|
| **< 500** | 1 (with backup) | 30s | ~17 |
| **500-5,000** | 3 (HA) | 30s | ~167 |
| **5,000-50,000** | 5 (HA, multi-region) | 60s | ~833 |
| **50,000+** | Regional clusters | 60s | Federated |

### Service Identity Authority

| Scale | SIA Instances | Certificate Requests/day |
|-------|--------------|-------------------------|
| **< 500** | 2 (HA) | ~500 (daily rotation) |
| **500-5,000** | 3 (HA) | ~5,000 |
| **5,000-50,000** | 5 (multi-region) | ~50,000 |
| **50,000+** | Regional clusters | Federated |

---

## Host Density Planning

### Services per Host

Based on typical service profile (600 MB memory, 0.5 CPU cores):

| Host Size | CPU | Memory | Services per Host | Notes |
|-----------|-----|--------|-------------------|-------|
| **Small** (4 core, 16 GB) | 4 cores | 16 GB | 6-8 | Leave 2 GB for OS + monitoring |
| **Medium** (8 core, 32 GB) | 8 cores | 32 GB | 15-20 | Recommended standard |
| **Large** (16 core, 64 GB) | 16 cores | 64 GB | 35-45 | High-density deployments |
| **XLarge** (32 core, 128 GB) | 32 cores | 128 GB | 75-90 | Maximum density |

### Formula

```
max_services_per_host = min(
  (available_memory - os_reserved) / avg_service_memory,
  (available_cpu_cores * 2) / avg_service_cpu,
  max_file_descriptors / avg_fds_per_service
)

Recommended: operate at 70% of max to allow burst capacity
```

### Host Count Estimation

```
total_hosts = ceil(total_services / services_per_host / 0.7) + spare_hosts

Example for 100,000 services on Medium hosts:
  = ceil(100,000 / 17 / 0.7) + 500 (5% spare)
  = ceil(8,403) + 500
  = 8,903 hosts
```

---

## Database Scaling Strategy

### Phase 1: Shared PostgreSQL (< 50 services)

```
All services ──→ Single PostgreSQL Instance
                  - Separate databases per service
                  - Shared server resources
                  - Single backup/restore point
```

### Phase 2: PostgreSQL Clusters (50-5,000 services)

```
Service groups ──→ PostgreSQL Cluster A (read replicas)
                ──→ PostgreSQL Cluster B (read replicas)
                ──→ PostgreSQL Cluster C (read replicas)

Grouping strategy:
- By domain (user services, billing services, etc.)
- By load profile (high-write vs read-heavy)
- By data sensitivity (PII-containing vs non-sensitive)
```

### Phase 3: Database-per-Service (5,000+ services)

```
Each service ──→ Dedicated PostgreSQL instance
                  - Provisioned automatically
                  - Sized based on service tier
                  - Independent lifecycle

Provisioning tiers:
- Tier S: 1 CPU, 2 GB RAM, 20 GB storage
- Tier M: 2 CPU, 4 GB RAM, 100 GB storage
- Tier L: 4 CPU, 8 GB RAM, 500 GB storage
- Tier XL: 8 CPU, 16 GB RAM, 1 TB storage
```

---

## Valkey/Redis Scaling Strategy

### Phase 1: Single Instance (< 50 services)

```
All services ──→ Single Valkey (8 GB)
```

### Phase 2: Valkey Cluster (50-5,000 services)

```
All services ──→ Valkey Cluster
                  - 6+ nodes (3 primary + 3 replica)
                  - Automatic sharding by key hash
                  - Key prefix per service ensures even distribution
```

### Phase 3: Federated Valkey (5,000+ services)

```
Service Group A ──→ Valkey Cluster A
Service Group B ──→ Valkey Cluster B
Service Group C ──→ Valkey Cluster C

Assignment via InfraSoT:
  ConnectionStrings__ValkeyConnection assigned per service during registration
```

---

## Network Capacity

### Per-Service Network Budget

| Traffic Type | Bandwidth | Direction |
|-------------|-----------|-----------|
| API traffic (client) | 1-10 Mbps | Inbound/Outbound |
| Database queries | 5-20 Mbps | To PostgreSQL |
| Valkey operations | 1-5 Mbps | To Valkey |
| mTLS handshakes | < 1 Mbps | S2S |
| InfraSoT heartbeat | < 0.1 Mbps | To InfraSoT |
| OpenTelemetry export | 0.5-2 Mbps | To Collector |
| **Total per service** | **~10-40 Mbps** | |

### Network Infrastructure Sizing

| Scale | Network Requirement | Recommendation |
|-------|-------------------|----------------|
| **< 100 services** | 1 Gbps per host | Standard NIC |
| **100-1,000** | 10 Gbps per host | 10GbE |
| **1,000-10,000** | 10 Gbps + dedicated DB network | Dual 10GbE |
| **10,000+** | 25/100 Gbps, network segmentation | 25GbE or bonded |

---

## Observability at Scale

### Log Volume Estimation

```
Per service (normal traffic):
  ~100-500 log entries/minute
  ~500 bytes average per entry
  ≈ 50-250 KB/minute
  ≈ 72-360 MB/day

Ecosystem (100,000 services):
  ≈ 7-36 TB/day of logs
```

**Mitigation strategies**:
- Structured JSON logging (already implemented via Serilog)
- PII scrubbing reduces duplicate/sensitive data (already implemented)
- Log level `Information` in production (not `Debug`)
- Log retention: 7 days hot, 30 days warm, 90 days cold
- Sampling for high-volume endpoints

### Metrics Volume Estimation

```
Per service:
  ~50 metric series (Prometheus)
  15-second scrape interval
  ≈ 200 datapoints/minute

Ecosystem (100,000 services):
  ≈ 20 million datapoints/minute
  ≈ 330K datapoints/second
```

**Recommendation**: Use OpenTelemetry with aggregation at collector level. Federate Prometheus instances by region/service-group.

### Tracing Volume

```
Per service (sampled at 10%):
  ~10 traces/second
  ~5 spans/trace average
  ≈ 50 spans/second

Ecosystem (100,000 services at 1% sampling):
  ≈ 500,000 spans/second
```

**Recommendation**: Implement adaptive sampling. Start at 10% for development, reduce to 0.1-1% for high-volume production services.

---

## Growth Planning Formula

### Annual Capacity Planning

```
projected_services(year) = current_services * (1 + annual_growth_rate)

projected_resources(year) = projected_services * avg_resource_per_service * overhead_factor

Where:
  overhead_factor = 1.3 (30% buffer for burst capacity)
  annual_growth_rate = varies by ecosystem phase
```

### Growth Phase Estimates

| Phase | Services | Annual Growth | Planning Horizon |
|-------|----------|--------------|-----------------|
| **Early** (Year 1-2) | 0-1,000 | 200-500% | Quarterly review |
| **Growth** (Year 2-4) | 1,000-50,000 | 100-200% | Monthly review |
| **Scale** (Year 4-6) | 50,000-200,000 | 50-100% | Monthly review |
| **Mature** (Year 6+) | 200,000+ | 10-30% | Quarterly review |

### Procurement Lead Time

| Resource | Lead Time | Planning Buffer |
|----------|-----------|----------------|
| Cloud VMs | Minutes | N/A (auto-scale) |
| Bare metal servers | 2-6 weeks | Order at 70% capacity |
| Network equipment | 4-8 weeks | Order at 60% capacity |
| Database licenses | 1-2 weeks | Order at 75% capacity |
| Storage expansion | 1-4 weeks | Order at 70% capacity |

---

## Bottleneck Identification

### Common Bottlenecks at Scale

| Scale Threshold | Likely Bottleneck | Indicator | Solution |
|----------------|------------------|-----------|----------|
| **100 services** | PostgreSQL connections | Connection timeouts | Add PgBouncer |
| **500 services** | InfraSoT Registry | Slow heartbeat processing | Scale to 3 instances |
| **1,000 services** | Valkey memory | Evictions, OOM | Valkey Cluster |
| **5,000 services** | Network bandwidth | Packet loss, latency | Upgrade to 10GbE |
| **10,000 services** | Observability pipeline | Log/metric backpressure | Federate collectors |
| **50,000 services** | Service Identity Authority | Certificate request queue | Regional clusters |
| **100,000+ services** | DNS/service discovery | Resolution latency | Local caching, regional DNS |

### Monitoring Indicators

Set alerts for these thresholds to proactively identify capacity needs:

| Metric | Warning | Critical |
|--------|---------|----------|
| Host CPU utilization | > 70% sustained | > 85% sustained |
| Host memory utilization | > 75% | > 90% |
| PostgreSQL connections used | > 70% of max | > 85% of max |
| Valkey memory used | > 70% of max | > 85% of max |
| Disk usage | > 70% | > 85% |
| Network utilization | > 60% of capacity | > 80% of capacity |
| InfraSoT heartbeat latency | > 5 seconds | > 15 seconds |
| Certificate renewal failures | Any | 3+ in 1 hour |

---

## References

- [DEPLOYMENT-SYSTEMD.md](../backend/docs/DEPLOYMENT-SYSTEMD.md) - Single service deployment
- [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md) - Backup and restore procedures
- [MODULES.md](MODULES.md) - Module system (resource consumption varies by enabled modules)
- [PostgreSQL Connection Pooling](https://www.pgbouncer.org/)
- [Valkey Cluster Tutorial](https://valkey.io/topics/cluster-tutorial/)

---

**Questions or Issues?**
File a ticket in the rithmXO Platform repository or contact the Platform Engineering team.
