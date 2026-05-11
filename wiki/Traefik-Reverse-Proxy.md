# Traefik Reverse Proxy

## What is Traefik?

Traefik is a modern reverse proxy and load balancer that auto-discovers routing configuration. In NexaCommerce it sits in front of all services and:

- Routes `/api/*` → ProductCatalog API
- Routes `/*` → Angular SPA
- Provides a dashboard showing all active routes
- Can terminate TLS, rate limit, and add middleware (auth, circuit breaker) in production

---

## Static vs Dynamic Configuration

Traefik splits configuration into two files:

| File | Purpose | Changes require |
|---|---|---|
| `traefik.yml` (static) | Entrypoints, providers, dashboard | Traefik restart |
| `dynamic.yml` (dynamic) | Routes, services, middlewares | Automatic reload — no restart |

```yaml
# infrastructure/traefik/traefik.yml  (static)
api:
  dashboard: true   # enables the Traefik dashboard at :8080
  insecure: true    # dashboard without auth (dev only — lock this down in prod)

entryPoints:
  web:
    address: ":8088"   # all traffic enters here

providers:
  file:
    filename: /etc/traefik/dynamic.yml
    watch: true   # reload dynamic config on file change
```

```yaml
# infrastructure/traefik/dynamic.yml  (dynamic)
http:
  routers:
    api-router:
      rule: "PathPrefix(`/api`)"    # match /api/products, /api/categories, etc.
      service: catalog-api
      entryPoints: [web]

    frontend-router:
      rule: "PathPrefix(`/`)"       # catch-all → Angular SPA
      service: frontend
      entryPoints: [web]
      priority: 1                   # lower priority than api-router

  services:
    catalog-api:
      loadBalancer:
        servers:
          - url: "http://catalog-api:5000"   # Docker service name

    frontend:
      loadBalancer:
        servers:
          - url: "http://frontend:4200"
```

---

## Aspire vs Docker Compose Config

Traefik is configured differently depending on how the stack runs:

**Via Aspire AppHost:**
```csharp
builder.AddContainer("traefik", cfg["Traefik:Tag"]!)
    .WithBindMount(cfg["Traefik:StaticConfigPath"]!, "/etc/traefik/traefik.yml", isReadOnly: true)
    .WithBindMount(cfg["Traefik:DynamicConfigPath"]!, "/etc/traefik/dynamic.yml", isReadOnly: true)
    .WithHttpEndpoint(port: int.Parse(cfg["Traefik:HttpPort"]!), targetPort: 8088)
    .WithHttpEndpoint(port: int.Parse(cfg["Traefik:DashboardPort"]!), targetPort: 8080);
```

**Via docker-compose:**
```yaml
traefik:
  image: traefik:v3
  ports:
    - "8088:8088"
    - "8081:8080"
  volumes:
    - ./infrastructure/traefik/traefik.yml:/etc/traefik/traefik.yml:ro
    - ./infrastructure/traefik/dynamic.yml:/etc/traefik/dynamic.yml:ro
```

---

## Traefik Dashboard

Access at `http://localhost:8081` (Aspire) or `http://localhost:8080` (docker-compose direct).

The dashboard shows:
- All active routers with their rules and linked services
- Health status of each backend service
- Middleware chains applied to each router
- Real-time request metrics

---

## Production Considerations

This scaffold uses Traefik in development mode (`insecure: true`). For production:

```yaml
# Static config — production
api:
  dashboard: true
  insecure: false   # require basic auth or mTLS for dashboard access

certificatesResolvers:
  letsencrypt:
    acme:
      email: ops@yourcompany.com
      storage: /acme.json
      httpChallenge:
        entryPoint: web
```

Add HTTPS middleware in dynamic config:
```yaml
middlewares:
  redirect-to-https:
    redirectScheme:
      scheme: https
      permanent: true
```
