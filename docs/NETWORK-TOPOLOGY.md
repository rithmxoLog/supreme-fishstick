# Network Topology and Enforcement Rules

**Version**: 1.0
**Last Updated**: 2026-02-16
**Audience**: DevOps, SRE, Network Engineers, Security Team

> ### Validation Required
>
> This document contains architectural proposals and templates that require validation:
>
> | Section | Needs Validation From | What to Validate |
> |---------|----------------------|------------------|
> | Platform service ports | **Platform Team** | Localhost defaults conflict between ServiceRouter and Platform services (see ServiceRouter Defaults section) |
> | Network zones | **Network Engineering / Security** | Zone names (DMZ, Application, Data, etc.) are a proposal — no VLAN/segmentation exists yet |
> | Firewall rules | **Network Engineering / Security** | All IP ranges are placeholders (`REVERSE_PROXY_IP`, `APPLICATION_NETWORK/24`, etc.) |
> | Nginx configuration | **SRE / Infrastructure** | Entire config is a template — no Nginx exists in the repo |
> | Rate limiting values | **SRE / Performance Engineering** | `100r/s` and `10r/s` are arbitrary — need load testing data |
> | DNS convention (`*.rithmxo.internal`) | **DNS / Infrastructure Team** | Not confirmed as a provisioned internal DNS zone |
> | Certificate chain (Root CA hierarchy) | **Security / PKI Team** | Described but not provisioned |
> | Port allocation convention | **Architecture / Platform Team** | Proposed standard, not confirmed |
>
> **Items that ARE grounded in the codebase**: Backend port 5000, Frontend port 3000,
> PostgreSQL 5432, Valkey 6379, AlertService 5100, OTEL 4317, systemd security restrictions,
> mTLS config in `appsettings.Production.json`.

---

## Table of Contents

1. [Network Architecture](#network-architecture)
2. [Service Port Assignments](#service-port-assignments)
3. [Traffic Flow Diagram](#traffic-flow-diagram)
4. [Firewall Rules](#firewall-rules)
5. [Reverse Proxy Configuration](#reverse-proxy-configuration)
6. [DNS and Service Discovery](#dns-and-service-discovery)
7. [mTLS Enforcement](#mtls-enforcement)
8. [systemd Network Restrictions](#systemd-network-restrictions)

---

## Network Architecture

### Overview

```
                           ┌──────────────────────────────────┐
                           │          EXTERNAL ZONE           │
                           │   (Internet / Client Networks)   │
                           └──────────────┬───────────────────┘
                                          │ HTTPS (443)
                                          ▼
                           ┌──────────────────────────────────┐
                           │         DMZ / EDGE ZONE          │
                           │                                  │
                           │  ┌────────────────────────────┐  │
                           │  │   Reverse Proxy / LB       │  │
                           │  │   (Nginx / HAProxy)        │  │
                           │  │   Ports: 80, 443           │  │
                           │  └─────────────┬──────────────┘  │
                           └────────────────┼─────────────────┘
                                            │
                      ┌─────────────────────┼─────────────────────┐
                      │                     │                     │
                      ▼                     ▼                     │
         ┌────────────────────┐  ┌────────────────────┐          │
         │  APPLICATION ZONE  │  │  APPLICATION ZONE  │          │
         │                    │  │                    │          │
         │  Frontend (3000)   │  │  Backend API (5000)│          │
         │  Next.js Standalone│  │  .NET 8 Service    │          │
         │                    │  │                    │          │
         └────────────────────┘  └────────┬───────────┘          │
                                          │                     │
                      ┌───────────────────┼─────────────────┐   │
                      │                   │                 │   │
                      ▼                   ▼                 ▼   │
         ┌──────────────────┐  ┌──────────────┐  ┌──────────┐  │
         │  DATA ZONE       │  │ CACHE ZONE   │  │ PLATFORM │  │
         │                  │  │              │  │ ZONE     │  │
         │  PostgreSQL      │  │  Valkey      │  │          │  │
         │  Port: 5432      │  │  Port: 6379  │  │ InfraSoT │  │
         │                  │  │              │  │ SIA      │  │
         │                  │  │              │  │ Policy   │  │
         │                  │  │              │  │ Audit    │  │
         │                  │  │              │  │ OTEL     │  │
         └──────────────────┘  └──────────────┘  └──────────┘  │
                                                                │
         ┌──────────────────────────────────────────────────────┘
         │  S2S ZONE (mTLS)
         │
         │  Service-to-service communication
         │  via ServiceRouter with mutual TLS
         │
         │  ┌──────────┐  mTLS  ┌──────────┐  mTLS  ┌──────────┐
         │  │Service A │◄──────►│Service B │◄──────►│Service C │
         │  │ (5000)   │        │ (5000)   │        │ (5000)   │
         │  └──────────┘        └──────────┘        └──────────┘
         │
         └───────────────────────────────────────────────────────
```

### Network Zones

| Zone | Purpose | Access |
|------|---------|--------|
| **External** | Client traffic from internet | HTTPS only via reverse proxy |
| **DMZ/Edge** | Reverse proxy, TLS termination | Accepts external HTTPS, proxies to Application Zone |
| **Application** | Service processes (systemd units) | Receives proxied traffic, initiates S2S and data calls |
| **Data** | PostgreSQL databases | Only from Application Zone |
| **Cache** | Valkey/Redis instances | Only from Application Zone |
| **Platform** | rithmXO infrastructure services | From Application Zone (mTLS) |
| **S2S** | Service-to-service communication | mTLS required between all services |

---

## Service Port Assignments

### Standard Ports

| Service | Port | Protocol | Zone | Description |
|---------|------|----------|------|-------------|
| **Reverse Proxy** | 80 | HTTP | Edge | Redirect to HTTPS |
| **Reverse Proxy** | 443 | HTTPS | Edge | TLS termination, client-facing |
| **Frontend** (Next.js) | 3000 | HTTP | Application | Next.js standalone server |
| **Backend API** (.NET) | 5000 | HTTP | Application | API endpoints, health, metrics |
| **PostgreSQL** | 5432 | TCP | Data | Database connections |
| **Valkey/Redis** | 6379 | TCP | Cache | Cache, locks, pub/sub |

### Platform Services (rithmXO Ecosystem)

> **NEEDS VALIDATION (Platform Team)**: The ports below are taken from `appsettings.json` defaults.
> Service Identity Authority has no hardcoded port (discovered via InfraSoT).
> OrchestratorXO port is not confirmed by the OrchestratorXO team.

| Service | Port | Source | Description |
|---------|------|--------|-------------|
| **AlertService** (SignalR) | 5100 | `appsettings.json` | Real-time notifications |
| **PolicyEngine** | 5300 | `appsettings.json` | Authorization policy evaluation |
| **AuditAuthority** | 5400 | `appsettings.json` | Audit trail service |
| **InfraSoT Registry** | 5500 | `appsettings.json` | Service discovery and registration |
| **Service Identity Authority** | Dynamic | Via InfraSoT | Certificate issuance (no hardcoded port) |
| **Observability Collector** | 4317 | `appsettings.json` | OpenTelemetry OTLP receiver (gRPC) |
| **Observability Collector** | 4318 | Standard | OpenTelemetry OTLP HTTP receiver |
| **OrchestratorXO** | TBD | Not confirmed | Workflow orchestration |

### ServiceRouter Defaults (Application-Level S2S)

> **WARNING: Localhost default port conflict** — In development (`localhost`), ServiceRouter
> defaults collide with Platform service defaults on the same ports. In production, these resolve
> to different hosts via configuration. The Platform Team should establish an authoritative port registry.

| Service | Default Port | Config Key | Conflict |
|---------|-------------|-----------|----------|
| **EmailService** | 5300 | `ServiceRouter:EmailServiceUrl` | Collides with PolicyEngine on localhost |
| **WebhookService** | 5400 | `ServiceRouter:WebhookServiceUrl` | Collides with AuditAuthority on localhost |
| **ContainerService** | 5500 | `ServiceRouter:ContainerServiceUrl` | Collides with InfraSoT on localhost |
| **DashboardService** | 5600 | `ServiceRouter:DashboardServiceUrl` | — |

**Resolution**: In production, all service URLs are configured explicitly via environment variables
or InfraSoT discovery, so localhost defaults are not used. However, for local development with
multiple services running, explicit port assignment in `appsettings.Development.json` is required.

### Port Allocation Convention (Proposed)

> **NEEDS VALIDATION (Architecture / Platform Team)**: This convention is a proposal and does
> not reflect a confirmed standard. The current codebase has conflicting defaults (see above).

```
3000-3099: Frontend applications
4317-4318: Observability (OpenTelemetry standard)
5000-5099: Application APIs (backend services)
5100-5199: Real-time services (SignalR, WebSocket)
5200-5299: Station/Device services
5300-5399: Security / Platform services
5400-5499: Compliance / Platform services
5500-5599: Infrastructure / Platform services
5600-5699: Orchestration services
```

---

## Traffic Flow Diagram

### Client Request Flow

```
Client (Browser/Mobile)
  │
  │ HTTPS (443)
  ▼
Reverse Proxy (Nginx)
  │
  ├── /api/*  ──→ Backend API (localhost:5000)  ──→ PostgreSQL (5432)
  │                    │                         ──→ Valkey (6379)
  │                    │                         ──→ Platform Services (mTLS)
  │                    │
  │                    └── ServiceRouter ──→ Other Services (mTLS)
  │
  └── /*      ──→ Frontend (localhost:3000)
```

### Service-to-Service Flow

```
Service A (Backend)
  │
  │ ServiceRouter (HTTP + mTLS)
  │ Headers: X-Tenant-Id, X-Org-Id, X-Correlation-Id
  │ Certificate: from Service Identity Authority
  ▼
Service B (Backend)
  │
  │ Validates:
  │ 1. Client certificate (mTLS)
  │ 2. JWT token (if present)
  │ 3. PolicyEngine authorization (if enabled)
  │ 4. Tenant isolation
  ▼
Response
```

### Observability Flow

```
Service (Backend)
  │
  ├── Structured logs ──→ journald ──→ Log Collector ──→ Centralized Logging
  │
  ├── OTLP traces ──→ Observability Collector (4317) ──→ Trace Backend
  │
  └── Prometheus metrics ──→ Scraper (/metrics endpoint on 5000)
```

---

## Firewall Rules

### Host-Level Firewall (iptables/nftables)

#### Application Host (runs backend + frontend)

```bash
#!/bin/bash
# /opt/rithm/scripts/configure-firewall.sh
# Apply firewall rules for a RithmTemplate application host

set -euo pipefail

# Flush existing rules
iptables -F INPUT
iptables -F OUTPUT

# Default policies
iptables -P INPUT DROP
iptables -P OUTPUT ACCEPT
iptables -P FORWARD DROP

# Allow loopback
iptables -A INPUT -i lo -j ACCEPT

# Allow established/related connections
iptables -A INPUT -m state --state ESTABLISHED,RELATED -j ACCEPT

# ──── INBOUND RULES ────

# SSH (restrict to management network)
iptables -A INPUT -p tcp --dport 22 -s MANAGEMENT_NETWORK/24 -j ACCEPT

# Reverse proxy to backend API (from Edge Zone only)
iptables -A INPUT -p tcp --dport 5000 -s REVERSE_PROXY_IP -j ACCEPT

# Reverse proxy to frontend (from Edge Zone only)
iptables -A INPUT -p tcp --dport 3000 -s REVERSE_PROXY_IP -j ACCEPT

# Service-to-service (from Application Zone)
iptables -A INPUT -p tcp --dport 5000 -s APPLICATION_NETWORK/24 -j ACCEPT

# Health check probes (from monitoring)
iptables -A INPUT -p tcp --dport 5000 -s MONITORING_NETWORK/24 -j ACCEPT

# Prometheus metrics scraping (from monitoring)
iptables -A INPUT -p tcp --dport 5000 -s PROMETHEUS_IP -j ACCEPT

# ──── OUTBOUND RULES (explicit for documentation) ────

# PostgreSQL (to Data Zone)
iptables -A OUTPUT -p tcp --dport 5432 -d DATABASE_NETWORK/24 -j ACCEPT

# Valkey (to Cache Zone)
iptables -A OUTPUT -p tcp --dport 6379 -d CACHE_NETWORK/24 -j ACCEPT

# InfraSoT (to Platform Zone)
iptables -A OUTPUT -p tcp --dport 5500 -d PLATFORM_NETWORK/24 -j ACCEPT

# Service Identity Authority
iptables -A OUTPUT -p tcp --dport 5501 -d PLATFORM_NETWORK/24 -j ACCEPT

# OpenTelemetry Collector
iptables -A OUTPUT -p tcp --dport 4317 -d OBSERVABILITY_NETWORK/24 -j ACCEPT

# DNS
iptables -A OUTPUT -p udp --dport 53 -j ACCEPT
iptables -A OUTPUT -p tcp --dport 53 -j ACCEPT

# NTP (time synchronization - critical for certificate validation)
iptables -A OUTPUT -p udp --dport 123 -j ACCEPT

# Log
iptables -A INPUT -j LOG --log-prefix "FW-DROP: " --log-level 4
iptables -A INPUT -j DROP

echo "Firewall rules applied successfully"
```

#### Database Host

```bash
# PostgreSQL host firewall
# Only accept connections from Application Zone

iptables -A INPUT -p tcp --dport 5432 -s APPLICATION_NETWORK/24 -j ACCEPT
iptables -A INPUT -p tcp --dport 5432 -j DROP
```

#### Cache Host

```bash
# Valkey host firewall
# Only accept connections from Application Zone

iptables -A INPUT -p tcp --dport 6379 -s APPLICATION_NETWORK/24 -j ACCEPT
iptables -A INPUT -p tcp --dport 6379 -j DROP
```

### Firewall Rule Summary Table

| Source | Destination | Port | Protocol | Action | Purpose |
|--------|------------|------|----------|--------|---------|
| Reverse Proxy | Backend | 5000 | TCP | ALLOW | API traffic |
| Reverse Proxy | Frontend | 3000 | TCP | ALLOW | Web UI |
| App Zone | App Zone | 5000 | TCP | ALLOW | S2S communication |
| App Zone | Database | 5432 | TCP | ALLOW | DB queries |
| App Zone | Cache | 6379 | TCP | ALLOW | Cache/locks |
| App Zone | Platform | 5500-5501 | TCP | ALLOW | InfraSoT, SIA |
| App Zone | Platform | 5300-5400 | TCP | ALLOW | PolicyEngine, Audit |
| App Zone | Observability | 4317 | TCP | ALLOW | OTLP export |
| Monitoring | App Zone | 5000 | TCP | ALLOW | Health/metrics |
| Management | All | 22 | TCP | ALLOW | SSH admin |
| External | Reverse Proxy | 443 | TCP | ALLOW | Client HTTPS |
| External | Reverse Proxy | 80 | TCP | ALLOW | Redirect to 443 |
| * | * | * | * | **DROP** | Default deny |

---

## Reverse Proxy Configuration

### Nginx Configuration Template

```nginx
# /etc/nginx/sites-available/rithm-template

# Redirect HTTP to HTTPS
server {
    listen 80;
    server_name app.rithmxo.internal;
    return 301 https://$server_name$request_uri;
}

# Main HTTPS server
server {
    listen 443 ssl http2;
    server_name app.rithmxo.internal;

    # TLS Configuration
    ssl_certificate     /etc/nginx/certs/server.crt;
    ssl_certificate_key /etc/nginx/certs/server.key;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_ciphers         HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    # Security Headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-XSS-Protection "1; mode=block" always;

    # API Backend
    location /api/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Timeouts for long-running operations
        proxy_connect_timeout 10s;
        proxy_read_timeout 300s;
        proxy_send_timeout 60s;

        # WebSocket support (for SignalR)
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }

    # Health checks (no auth required)
    location /health/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
    }

    # Metrics (restrict to monitoring network)
    location /metrics {
        allow MONITORING_NETWORK/24;
        deny all;
        proxy_pass http://127.0.0.1:5000;
    }

    # Swagger (restrict to development/internal)
    location /swagger {
        allow INTERNAL_NETWORK/24;
        deny all;
        proxy_pass http://127.0.0.1:5000;
    }

    # Frontend (Next.js)
    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Static assets caching
    location /_next/static/ {
        proxy_pass http://127.0.0.1:3000;
        expires 1y;
        add_header Cache-Control "public, immutable";
    }
}
```

### Rate Limiting

```nginx
# Add to nginx.conf http block
limit_req_zone $binary_remote_addr zone=api:10m rate=100r/s;
limit_req_zone $binary_remote_addr zone=auth:10m rate=10r/s;

# Apply in location blocks
location /api/ {
    limit_req zone=api burst=50 nodelay;
    # ... proxy_pass config
}

location /api/auth/ {
    limit_req zone=auth burst=5 nodelay;
    # ... proxy_pass config
}
```

---

## DNS and Service Discovery

### Internal DNS Convention

```
{service-name}.rithmxo.internal

Examples:
  rithm-template.rithmxo.internal        → Application host
  db.rithm-template.rithmxo.internal     → Database host
  cache.rithm-template.rithmxo.internal  → Valkey host
  infrasot-registry.rithmxo.internal     → InfraSoT Registry
  orchestrator-xo.rithmxo.internal       → OrchestratorXO
```

### InfraSoT Service Discovery

Services discover each other via InfraSoT (not DNS alone):

```
1. Service starts
2. Queries InfraSoT: GET /api/services/{target-service-id}
3. Receives: { "baseUrl": "https://target.rithmxo.internal:5000", "status": "healthy" }
4. Caches location with TTL
5. Calls target via ServiceRouter with mTLS
```

**Fallback**: If InfraSoT is unavailable, services can use DNS as fallback for known infrastructure services (PostgreSQL, Valkey).

---

## mTLS Enforcement

### Certificate Chain

```
Root CA (rithmXO Ecosystem)
  └── Intermediate CA (Service Identity Authority)
        ├── Service A Certificate (CN=service-a, SAN=service-a.rithmxo.internal)
        ├── Service B Certificate (CN=service-b, SAN=service-b.rithmxo.internal)
        └── OrchestratorXO Certificate (CN=orchestrator-xo)
```

### Enforcement by Environment

| Environment | Client → Proxy | Proxy → Service | Service → Service | Service → DB |
|-------------|---------------|-----------------|-------------------|-------------|
| **Development** | HTTP (localhost) | HTTP | HTTP (no mTLS) | TCP |
| **Staging** | HTTPS | HTTP (internal) | mTLS | TCP + SSL |
| **Production** | HTTPS | HTTP (internal) | **mTLS (required)** | TCP + SSL |

### Validation Rules

All mTLS connections validate:

1. **Certificate not expired** (`NotBefore` < now < `NotAfter`)
2. **Certificate chain valid** (traces to rithmXO Root CA)
3. **Subject matches** expected service identity
4. **No revocation** (if CRL/OCSP is configured)

---

## systemd Network Restrictions

### Built-in Restrictions

The systemd unit files include network-level security restrictions:

```ini
# From rithm-template.service

# Restrict address families to IPv4/IPv6 only (no raw sockets, no netlink)
RestrictAddressFamilies=AF_INET AF_INET6

# Restrict namespaces (cannot create new network namespaces)
RestrictNamespaces=yes

# Private /tmp (isolated from other services)
PrivateTmp=yes

# Cannot load kernel modules
ProtectKernelModules=yes

# Read-only system paths
ProtectSystem=strict

# No access to /home
ProtectHome=yes

# No access to hardware devices
PrivateDevices=yes
```

### What These Restrict

| Restriction | Prevents |
|------------|---------|
| `RestrictAddressFamilies=AF_INET AF_INET6` | Raw sockets, Bluetooth, CAN bus, Netlink, Unix domain sockets |
| `RestrictNamespaces=true` | Creating new network/PID/mount namespaces |
| `PrivateDevices=yes` | Direct hardware device access |
| `ProtectKernelTunables=yes` | Modifying sysctl values |
| `ProtectControlGroups=yes` | Modifying cgroup hierarchy |
| `NoNewPrivileges=yes` | Privilege escalation via setuid/setgid |

---

## References

- [DEPLOYMENT-SYSTEMD.md](../backend/docs/DEPLOYMENT-SYSTEMD.md) - Service deployment with systemd
- [PLAN-CERTIFICADOS-RITHMXO.md](../backend/docs/PLAN-CERTIFICADOS-RITHMXO.md) - Certificate management
- [ORCHESTRATORXO-COMPATIBILITY.md](ORCHESTRATORXO-COMPATIBILITY.md) - OrchestratorXO integration
- [Nginx Reverse Proxy Documentation](https://nginx.org/en/docs/http/ngx_http_proxy_module.html)
- [systemd Security Features](https://www.freedesktop.org/software/systemd/man/systemd.exec.html)

---

**Questions or Issues?**
File a ticket in the rithmXO Platform repository or contact the Network/Security team.
