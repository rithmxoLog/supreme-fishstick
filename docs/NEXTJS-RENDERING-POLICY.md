# Next.js Rendering Policy

**Version**: 1.0
**Last Updated**: 2026-02-16
**Audience**: Frontend Developers
**Framework**: Next.js 14.2 (App Router) with `output: 'standalone'`

> ### Validation Required
>
> | Section | Needs Validation From | What to Validate |
> |---------|----------------------|------------------|
> | SSR/ISR/SSG examples | **Frontend Team** | Examples are illustrative — not implemented in the template yet |
> | Server-side fetch patterns | **Frontend Team** | `api-server.ts` does not exist yet — proposed pattern |
> | ISR revalidation values (e.g. 300s) | **Frontend / Product** | Example values — actual freshness requirements are per-page |
> | Nginx static asset caching | **SRE / Infrastructure** | References Nginx template from NETWORK-TOPOLOGY.md (not deployed) |
>
> **Items that ARE grounded in the codebase**: Next.js 14.2 + `output: 'standalone'`
> (`next.config.js`), React Query defaults (`query-client.ts`), Axios client with
> `NEXT_PUBLIC_API_URL` (`axios.ts`), systemd unit config (`rithm-template-web.service`),
> CSR pattern with React Query hooks.

---

## Table of Contents

1. [Current Setup](#current-setup)
2. [Rendering Strategies](#rendering-strategies)
3. [Decision Matrix](#decision-matrix)
4. [Data Fetching Patterns](#data-fetching-patterns)
5. [Caching Strategy](#caching-strategy)
6. [Deployment Considerations](#deployment-considerations)

---

## Current Setup

### Configuration

The RithmTemplate frontend runs as a **Next.js standalone server** deployed via systemd:

```js
// next.config.js
const nextConfig = {
  output: 'standalone',    // Self-contained Node.js server (no next start dependency)
  reactStrictMode: true,
  images: { unoptimized: true },  // No image optimization server needed
};
```

### Stack

| Library | Role |
|---------|------|
| **Next.js 14.2** (App Router) | Framework, routing, rendering |
| **React 18.3** | UI library |
| **TanStack React Query 5** | Server state management, caching, data fetching |
| **Axios** | HTTP client for API calls (`NEXT_PUBLIC_API_URL`) |
| **React Hook Form** | Form state management |
| **Tailwind CSS** | Styling |

### Current Rendering Model

The template currently uses **Client-Side Rendering (CSR)** with React Query for all data fetching. This is the baseline; the policy below documents when to use each strategy as features grow.

---

## Rendering Strategies

Next.js App Router provides four rendering strategies. Each has specific use cases within rithmXO services.

### 1. Client-Side Rendering (CSR)

**How**: `'use client'` directive + React Query hooks

**When to use**:
- User-specific data behind authentication (dashboards, profiles)
- Real-time or frequently changing data
- Interactive forms and widgets
- Data that depends on user actions or client state

**Example** (current template pattern):

```tsx
'use client';

import { useSampleEntityList } from '@/features/sample-entities';

export default function EntitiesPage() {
  const { data, isLoading, error } = useSampleEntityList();

  if (isLoading) return <LoadingSkeleton />;
  if (error) return <ErrorMessage error={error} />;

  return <EntityTable data={data} />;
}
```

**Pros**: Simple, works with auth tokens, great for interactive UIs
**Cons**: Initial load shows skeleton/spinner, no SEO benefit

---

### 2. Server-Side Rendering (SSR)

**How**: `async` Server Component with `fetch` (no caching) or `cookies()`/`headers()`

**When to use**:
- Pages that need SEO but also show fresh data
- Pages that depend on request-specific data (cookies, headers)
- Admin panels with tenant-specific content that must be indexed

**Example**:

```tsx
// app/public/entities/[id]/page.tsx
// Server Component (no 'use client' directive)

import { getEntity } from '@/lib/api-server';

export default async function EntityDetailPage({
  params,
}: {
  params: { id: string };
}) {
  const entity = await getEntity(params.id);

  return (
    <article>
      <h1>{entity.name}</h1>
      <p>{entity.description}</p>
    </article>
  );
}
```

**Pros**: SEO-friendly, fresh data on every request
**Cons**: Slower TTFB (server must fetch data before sending HTML)

---

### 3. Static Site Generation (SSG)

**How**: Server Component with `fetch` + `cache: 'force-cache'`, or `generateStaticParams`

**When to use**:
- Public pages that rarely change (landing, docs, about)
- Content that is the same for all users/tenants
- Pages where build-time data is acceptable

**Example**:

```tsx
// app/docs/[slug]/page.tsx

export async function generateStaticParams() {
  return [
    { slug: 'getting-started' },
    { slug: 'api-reference' },
    { slug: 'faq' },
  ];
}

export default async function DocPage({
  params,
}: {
  params: { slug: string };
}) {
  const content = await getDocContent(params.slug);
  return <MarkdownRenderer content={content} />;
}
```

**Pros**: Fastest possible response, zero server compute per request
**Cons**: Stale data until next build, not suitable for dynamic/auth content

---

### 4. Incremental Static Regeneration (ISR)

**How**: Server Component with `fetch` + `next: { revalidate: seconds }`

**When to use**:
- Public-facing pages that change periodically (product catalogs, listings)
- Content that can be slightly stale (1-60 minutes)
- High-traffic pages where SSR would be too expensive

**Example**:

```tsx
// app/catalog/page.tsx

async function getCatalog() {
  const res = await fetch('http://localhost:5000/api/catalog', {
    next: { revalidate: 300 }, // Revalidate every 5 minutes
  });
  return res.json();
}

export default async function CatalogPage() {
  const catalog = await getCatalog();
  return <CatalogGrid items={catalog} />;
}
```

**Pros**: Fast responses (cached), data freshness within revalidation window
**Cons**: Data can be stale up to `revalidate` seconds

---

## Decision Matrix

Use this table to choose the right rendering strategy:

| Criteria | CSR | SSR | SSG | ISR |
|----------|-----|-----|-----|-----|
| **Needs authentication** | Yes | Possible | No | No |
| **SEO required** | No | Yes | Yes | Yes |
| **Data freshness** | Real-time | Per-request | Build-time | Periodic |
| **User-specific data** | Yes | Yes | No | No |
| **High traffic** | Fine | Expensive | Best | Good |
| **Interactive UI** | Best | OK (hydrate) | OK (hydrate) | OK (hydrate) |

### Quick Decision Flow

```
Does the page require authentication/user-specific data?
  ├── YES → Use CSR (React Query)
  └── NO
       │
       Does the page need SEO?
       ├── NO → Use CSR (React Query)
       └── YES
            │
            Does the data change frequently?
            ├── NO (rarely/never) → Use SSG
            ├── SOMETIMES (hourly/daily) → Use ISR
            └── YES (every request) → Use SSR
```

### RithmTemplate Recommendations

| Page Type | Strategy | Reason |
|-----------|----------|--------|
| **Dashboard** (authenticated) | CSR | User-specific, behind auth, real-time data |
| **Entity CRUD** (authenticated) | CSR | Interactive forms, user actions |
| **Public landing page** | SSG | Static content, best performance |
| **Public entity detail** | ISR | SEO needed, data changes periodically |
| **Docs/Help pages** | SSG | Static content |
| **Admin settings** | CSR | Authenticated, interactive |
| **Status/health page** | ISR | Public, updates periodically |

---

## Data Fetching Patterns

### Pattern 1: Client-Side with React Query (Primary)

For authenticated pages — this is the default pattern in the template.

```tsx
// features/sample-entities/hooks/useSampleEntityList.ts
'use client';

import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/axios';

export function useSampleEntityList() {
  return useQuery({
    queryKey: ['sample-entities'],
    queryFn: () => api.get('/sampleentities').then(res => res.data),
    // Uses global defaults from query-client.ts:
    // staleTime: 5 min, gcTime: 30 min, retry: 1
  });
}
```

### Pattern 2: Server-Side Fetch (For SSR/ISR/SSG)

For public pages rendered on the server:

```tsx
// lib/api-server.ts
// Server-only utilities (never imported in 'use client' components)

// Uses the same NEXT_PUBLIC_API_URL env var configured in systemd unit
// Production: http://127.0.0.1:5000/api (set in rithm-template-web.service)
const API_BASE = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

export async function fetchFromApi<T>(
  path: string,
  options?: { revalidate?: number }
): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    next: options?.revalidate
      ? { revalidate: options.revalidate }
      : undefined,
  });

  if (!res.ok) {
    throw new Error(`API error: ${res.status}`);
  }

  return res.json();
}
```

### Pattern 3: Hybrid (Server Component + Client Component)

Combine server rendering for initial data with client interactivity:

```tsx
// app/entities/page.tsx (Server Component)
import { fetchFromApi } from '@/lib/api-server';
import { EntityListClient } from './entity-list-client';

export default async function EntitiesPage() {
  // Fetch initial data on server
  const initialData = await fetchFromApi('/api/sampleentities/public');

  // Pass to client component for interactivity
  return <EntityListClient initialData={initialData} />;
}

// app/entities/entity-list-client.tsx (Client Component)
'use client';

import { useQuery } from '@tanstack/react-query';

export function EntityListClient({ initialData }) {
  const { data } = useQuery({
    queryKey: ['entities'],
    queryFn: fetchEntities,
    initialData,  // Use server-fetched data as initial
  });

  return <InteractiveEntityList data={data} />;
}
```

---

## Caching Strategy

### React Query Cache (Client-Side)

Configured in [query-client.ts](../frontend/src/lib/query-client.ts):

| Setting | Value | Purpose |
|---------|-------|---------|
| `staleTime` | 5 minutes (`1000 * 60 * 5`) | How long data is considered fresh |
| `gcTime` | 30 minutes (`1000 * 60 * 30`) | How long unused data stays in cache |
| `refetchOnWindowFocus` | false | Does NOT refresh when user returns to tab |
| `retry` (queries) | 1 | Retry failed queries once |
| `retry` (mutations) | 0 | No retry for mutations |

### Next.js Cache (Server-Side)

| Strategy | Cache Behavior |
|----------|---------------|
| SSG | Cached at build time, served from disk |
| ISR | Cached, revalidated after `revalidate` seconds |
| SSR | Not cached (fresh on every request) |
| CSR | N/A (data fetched client-side, React Query handles cache) |

---

## Deployment Considerations

### Standalone Output

The `output: 'standalone'` configuration creates a self-contained Node.js server:

```
artifacts/frontend/
├── .next/
│   └── standalone/
│       ├── server.js          # Entry point
│       ├── node_modules/      # Minimal dependencies
│       └── .next/
│           └── static/        # Pre-built static assets
└── public/                    # Public files
```

This runs as a systemd service (`rithm-template-web.service`) on port 3000.

### Static Assets

Static assets (`/_next/static/`) are immutable and should be cached aggressively by the reverse proxy:

```nginx
location /_next/static/ {
    proxy_pass http://127.0.0.1:3000;
    expires 1y;
    add_header Cache-Control "public, immutable";
}
```

### Environment Variables

Configured in the systemd unit file ([rithm-template-web.service](../backend/deployment/systemd/rithm-template-web.service)):

```ini
# From rithm-template-web.service
Environment=NODE_ENV=production
Environment=PORT=3000
Environment=HOSTNAME=0.0.0.0
Environment=NEXT_PUBLIC_API_URL=http://127.0.0.1:5000/api
```

`NEXT_PUBLIC_API_URL` is used both client-side (via Next.js public env var prefix) and in the Axios client ([axios.ts](../frontend/src/lib/axios.ts)).

---

## References

- [Next.js App Router Documentation](https://nextjs.org/docs/app)
- [React Query Documentation](https://tanstack.com/query/latest)
- [NETWORK-TOPOLOGY.md](NETWORK-TOPOLOGY.md) - Reverse proxy configuration
- [DEPLOYMENT-SYSTEMD.md](../backend/docs/DEPLOYMENT-SYSTEMD.md) - systemd deployment

---

**Questions or Issues?**
File a ticket in the rithmXO Platform repository or contact the Frontend team.
