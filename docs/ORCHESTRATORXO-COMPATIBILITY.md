# OrchestratorXO Compatibility Strategy

**Version**: 1.0
**Last Updated**: 2026-02-16
**Audience**: Service Developers, Platform Engineers, Integration Teams
**Status**: Living document - update as OrchestratorXO evolves

> ### Validation Required
>
> **This entire document is a proposal** for how RithmTemplate services should integrate with
> OrchestratorXO. It must be validated with the OrchestratorXO team before implementation.
>
> | Section | Needs Validation From | What to Validate |
> |---------|----------------------|------------------|
> | Discovery via InfraSoT | **OrchestratorXO Team** | Will OrchestratorXO use InfraSoT or its own discovery? |
> | mTLS / Service Identity | **OrchestratorXO Team / Security** | Will OrchestratorXO adopt rithmXO Service Identity Authority? |
> | Request headers (`X-Workflow-Id`, etc.) | **OrchestratorXO Team** | Header names are proposed — not confirmed by OrchestratorXO API spec |
> | Webhook callbacks (`X-Callback-Url`) | **OrchestratorXO Team** | Not implemented on either side — proposed pattern |
> | `GET /api/operations/{id}` | **Service Architects** | Endpoint does not exist in RithmTemplate yet — needs implementation |
> | `DELETE /api/operations/{id}` | **Service Architects** | Cancellation not implemented — proposed pattern |
> | Compensation endpoints | **Service Architects / OrchestratorXO** | Saga rollback is not implemented — future design |
> | 4-phase migration path | **OrchestratorXO Team / Platform** | Proposed roadmap, not agreed-upon plan |
>
> **Items that ARE grounded in the codebase**: 202 Accepted pattern, IdempotencyMiddleware,
> BatchProcessing module, standard rithmXO headers (`X-Tenant-Id`, `X-Org-Id`, `X-Correlation-Id`),
> InfraSoT registration, health endpoints.

---

## Table of Contents

1. [Context](#context)
2. [Integration Model](#integration-model)
3. [Communication Contract](#communication-contract)
4. [Operation Lifecycle](#operation-lifecycle)
5. [Service Compliance Checklist](#service-compliance-checklist)
6. [Migration Path](#migration-path)
7. [Coexistence Strategy](#coexistence-strategy)

---

## Context

### What is OrchestratorXO?

OrchestratorXO is an existing orchestration application being adapted to operate within the rithmXO ecosystem. It coordinates multi-service workflows, long-running processes, and cross-service transactions.

### Relationship with RithmTemplate

RithmTemplate-based services are **participants** in OrchestratorXO workflows:

```
OrchestratorXO                          RithmTemplate Services
┌──────────────────────┐                ┌──────────────────────┐
│                      │   HTTP/mTLS    │                      │
│  Workflow Engine     │───────────────→│  Service A           │
│                      │                │  (202 Accepted)      │
│  - Defines workflows │   HTTP/mTLS    ├──────────────────────┤
│  - Tracks progress   │───────────────→│  Service B           │
│  - Handles failures  │                │  (202 Accepted)      │
│  - Manages retries   │   HTTP/mTLS    ├──────────────────────┤
│                      │───────────────→│  Service C           │
│                      │                │  (Immediate 200)     │
└──────────┬───────────┘                └──────────────────────┘
           │
           │ Registers as citizen
           ▼
┌──────────────────────┐
│   InfraSoT Registry  │
└──────────────────────┘
```

### Key Principles

1. **Services remain autonomous**: OrchestratorXO coordinates, it does not own service logic
2. **Standard HTTP contracts**: Communication via REST APIs (no proprietary protocols)
3. **202 Accepted pattern**: Long-running operations use the pattern already built into RithmTemplate
4. **mTLS for all communication**: OrchestratorXO is a rithmXO citizen with Service Identity
5. **InfraSoT discovery**: OrchestratorXO discovers services via InfraSoT, not hardcoded URLs

---

## Integration Model

### Discovery

OrchestratorXO locates services through InfraSoT:

```
1. OrchestratorXO starts
2. Registers itself with InfraSoT as "orchestrator-xo"
3. Discovers target services: GET /api/services/{service-id}
4. Caches service locations with TTL
5. Re-discovers on cache miss or connection failure
```

### Authentication

All communication uses the rithmXO standard security stack:

| Layer | Mechanism |
|-------|-----------|
| **Transport** | mTLS (mutual TLS via Service Identity Authority) |
| **Identity** | OrchestratorXO presents its service certificate |
| **Authorization** | Target service validates via PolicyEngine (if enabled) |
| **Tenant context** | `X-Tenant-Id` and `X-Org-Id` headers propagated |
| **Correlation** | `X-Correlation-Id` propagated across all calls |

### Request Headers

OrchestratorXO must include these headers on every request to a RithmTemplate service:

```http
POST /api/sampleentities/bulk-import HTTP/1.1
Host: service-a.rithmxo.internal:5000
Content-Type: application/json

# Required rithmXO headers
X-Tenant-Id: 00000000-0000-0000-0000-000000000001
X-Org-Id: org-123
X-Actor-Id: orchestrator-xo@system
X-Correlation-Id: workflow-run-abc-123

# Idempotency (prevents duplicate processing on retry)
Idempotency-Key: wf-abc-123-step-3-attempt-1

# OrchestratorXO-specific context (optional, for observability)
X-Workflow-Id: workflow-abc-123
X-Workflow-Step: 3
X-Workflow-Name: user-onboarding
```

---

## Communication Contract

### Pattern 1: Immediate Operations (Synchronous)

For fast operations (< 1 second):

```
OrchestratorXO                    Service
     │                               │
     │  POST /api/resource           │
     │──────────────────────────────→│
     │                               │ (process immediately)
     │  200 OK / 201 Created         │
     │←──────────────────────────────│
     │                               │
```

**Service response**:
```json
{
  "id": "resource-uuid",
  "name": "Created Resource",
  "createdAt": "2026-02-16T10:00:00Z"
}
```

### Pattern 2: Long-Running Operations (Asynchronous)

For operations > 1 second (bulk imports, complex processing):

```
OrchestratorXO                    Service                     Valkey
     │                               │                          │
     │  POST /api/resource/bulk      │                          │
     │──────────────────────────────→│                          │
     │                               │  Store operation state   │
     │                               │─────────────────────────→│
     │  202 Accepted                 │                          │
     │  Location: /operations/{id}   │                          │
     │←──────────────────────────────│                          │
     │                               │                          │
     │  (background processing)      │                          │
     │                               │  Update progress         │
     │                               │─────────────────────────→│
     │                               │                          │
     │  GET /operations/{id}         │                          │
     │──────────────────────────────→│                          │
     │                               │  Read operation state    │
     │                               │─────────────────────────→│
     │  200 OK (status: running)     │                          │
     │←──────────────────────────────│                          │
     │                               │                          │
     │  ... (poll or webhook) ...    │                          │
     │                               │                          │
     │  GET /operations/{id}         │                          │
     │──────────────────────────────→│                          │
     │  200 OK (status: completed)   │                          │
     │←──────────────────────────────│                          │
```

**202 Accepted response**:
```json
{
  "operationId": "op-uuid-123",
  "status": "accepted",
  "statusUrl": "/api/operations/op-uuid-123",
  "estimatedCompletionTime": "2026-02-16T10:05:00Z"
}
```

**Operation status response** (polling):
```json
{
  "operationId": "op-uuid-123",
  "status": "running",
  "progress": {
    "current": 450,
    "total": 1000,
    "percentage": 45
  },
  "startedAt": "2026-02-16T10:00:00Z",
  "updatedAt": "2026-02-16T10:02:30Z"
}
```

**Completed status**:
```json
{
  "operationId": "op-uuid-123",
  "status": "completed",
  "progress": {
    "current": 1000,
    "total": 1000,
    "percentage": 100
  },
  "result": {
    "processed": 1000,
    "succeeded": 985,
    "failed": 15,
    "errors": [
      { "index": 42, "error": "Duplicate entry" },
      { "index": 99, "error": "Validation failed: name required" }
    ]
  },
  "startedAt": "2026-02-16T10:00:00Z",
  "completedAt": "2026-02-16T10:04:15Z"
}
```

### Pattern 3: Webhook Notification (Optional)

If the service supports SignalR or webhook callbacks, OrchestratorXO can register for notifications instead of polling:

```http
POST /api/resource/bulk-import HTTP/1.1
X-Callback-Url: https://orchestrator-xo.rithmxo.internal/api/callbacks/wf-abc-123
X-Callback-Events: completed,failed
```

---

## Operation Lifecycle

### States

```
        ┌──────────┐
        │ accepted │  (202 response sent)
        └────┬─────┘
             │
        ┌────▼─────┐
        │ running  │  (processing in background)
        └────┬─────┘
             │
     ┌───────┼───────┐
     │       │       │
┌────▼──┐ ┌─▼────┐ ┌▼────────┐
│completed│ │failed│ │cancelled│
└────────┘ └──────┘ └─────────┘
```

### OrchestratorXO Responsibilities

| Responsibility | How |
|---------------|-----|
| **Start operation** | POST to service endpoint with idempotency key |
| **Track progress** | Poll status URL or listen for webhook |
| **Handle timeout** | Cancel operation after configurable timeout |
| **Handle failure** | Retry with new idempotency key (new attempt) |
| **Compensate** | Call service's compensation endpoint on workflow failure |

### Service Responsibilities (RithmTemplate)

| Responsibility | Implementation |
|---------------|---------------|
| **Accept operation** | Return 202 with operation ID and status URL |
| **Track progress** | Update operation state in Valkey via `MassiveOperationManager` |
| **Support polling** | Expose `GET /api/operations/{id}` endpoint |
| **Idempotency** | Deduplicate via `Idempotency-Key` header |
| **Cancellation** | Support `DELETE /api/operations/{id}` (best effort) |
| **Orphan recovery** | BatchProcessing module auto-recovers orphaned operations |

---

## Service Compliance Checklist

For a RithmTemplate-based service to be fully compatible with OrchestratorXO:

### Required (Minimum Viable)

- [ ] **InfraSoT registration enabled** (`InfraSoT__EnableRegistration=true`)
- [ ] **Health endpoints exposed** (`/health/live`, `/health/ready`)
- [ ] **Standard headers accepted** (`X-Tenant-Id`, `X-Org-Id`, `X-Actor-Id`, `X-Correlation-Id`)
- [ ] **Idempotency support** (IdempotencyMiddleware enabled)
- [ ] **mTLS enabled** for production (`MutualTLS__Enabled=true`)
- [ ] **RFC 7807 error responses** (GlobalExceptionHandler - already in template)

### Recommended (For Long-Running Operations)

- [ ] **202 Accepted pattern** for operations > 1 second
- [ ] **Operation status endpoint** (`GET /api/operations/{id}`)
- [ ] **BatchProcessing module enabled** (`Core.BatchProcessing: Enabled`)
- [ ] **Progress tracking** via `MassiveOperationManager`
- [ ] **Orphan recovery** enabled (automatic with BatchProcessing module)

### Optional (Enhanced Integration)

- [ ] **Cancellation endpoint** (`DELETE /api/operations/{id}`)
- [ ] **Webhook callbacks** via SignalR module
- [ ] **Compensation endpoints** for saga rollback scenarios
- [ ] **Operation metadata** (accept `X-Workflow-Id`, `X-Workflow-Step` headers for tracing)

---

## Migration Path

### Phase 1: Registration and Discovery

**Goal**: OrchestratorXO can find and call existing services.

**Service changes**: None (InfraSoT registration is already part of the template).

**OrchestratorXO changes**:
- Register as rithmXO citizen in InfraSoT
- Discover services via InfraSoT API
- Include rithmXO standard headers in all requests

### Phase 2: Standard Communication

**Goal**: OrchestratorXO uses rithmXO security stack.

**Service changes**: Ensure mTLS and PolicyEngine modules are enabled in production.

**OrchestratorXO changes**:
- Obtain service certificate from Service Identity Authority
- Present certificate on all service calls (mTLS)
- Propagate tenant context headers

### Phase 3: Asynchronous Operation Support

**Goal**: OrchestratorXO can manage long-running operations.

**Service changes**:
- Enable BatchProcessing module
- Implement `GET /api/operations/{id}` for operation status
- Return 202 Accepted with operation tracking info

**OrchestratorXO changes**:
- Handle 202 Accepted responses
- Implement polling loop for operation status
- Configure per-operation timeouts

### Phase 4: Advanced Workflows

**Goal**: Full workflow orchestration with compensation and retries.

**Service changes**:
- Implement compensation endpoints where needed
- Accept cancellation requests for in-progress operations
- Forward `X-Workflow-Id` to logs for distributed tracing

**OrchestratorXO changes**:
- Implement saga pattern with compensation
- Implement retry policies per operation type
- Support parallel step execution

---

## Coexistence Strategy

### During Migration

Services can operate both independently and as OrchestratorXO participants simultaneously:

```
Direct Client ──→ Service A (works as always)
                       ↑
OrchestratorXO ────────┘ (same API, same contract)
```

**Key principle**: The API contract is the same regardless of caller. OrchestratorXO is just another client that happens to include workflow context headers.

### Header Handling

Services should handle OrchestratorXO headers gracefully:

```csharp
// In controller or middleware - workflow headers are optional
var workflowId = Request.Headers["X-Workflow-Id"].FirstOrDefault();
var workflowStep = Request.Headers["X-Workflow-Step"].FirstOrDefault();

// Include in logs if present (for distributed tracing)
if (!string.IsNullOrEmpty(workflowId))
{
    _logger.LogInformation(
        "Processing as part of workflow {WorkflowId}, step {Step}",
        workflowId, workflowStep);
}
```

### Versioning

When OrchestratorXO requires API changes in services:

1. **Additive changes only**: New optional fields, new endpoints
2. **No breaking changes**: Existing endpoints continue to work
3. **Version via headers**: `Accept: application/vnd.rithm.v2+json` (if needed)
4. **Feature detection**: OrchestratorXO checks `/health/ready` for available capabilities

---

## References

- [ARCHITECTURE.md](../backend/ARCHITECTURE.md) - Service architecture and patterns
- [MODULES.md](MODULES.md) - Module system (BatchProcessing, SignalR)
- [DEPLOYMENT-SYSTEMD.md](../backend/docs/DEPLOYMENT-SYSTEMD.md) - Service deployment
- [NETWORK-TOPOLOGY.md](NETWORK-TOPOLOGY.md) - Network architecture and ports

---

**Questions or Issues?**
File a ticket in the rithmXO Platform repository or contact the Integration team.
