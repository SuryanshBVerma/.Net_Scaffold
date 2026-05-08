# Traefik Plugins — WASM Custom Middleware

## What is a Traefik Plugin?

Traefik plugins let you write custom middleware in **Go** (compiled to WebAssembly) and load
it into Traefik without recompiling Traefik itself. They run at the proxy layer — before any
request reaches your application.

Use plugins when you need behaviour that built-in middleware can't provide:
- Custom JWT claim validation (check a specific claim, not just verify the token)
- Request signing / HMAC verification
- Custom rate limiting (per tenant, per plan, per IP range)
- Dynamic header injection from an external config source

## Plugin Structure

```
plugins/
└── my-plugin/
    ├── go.mod
    ├── plugin.go        ← Must implement the Traefik plugin interface
    └── README.md
```

`plugin.go` must implement:
```go
// New creates a new plugin instance. Called once per router that uses it.
func New(ctx context.Context, next http.Handler, config *Config, name string) (http.Handler, error)

// ServeHTTP handles each request. Call next.ServeHTTP to pass through.
func (p *Plugin) ServeHTTP(rw http.ResponseWriter, req *http.Request)
```

## Registering a Local Plugin

In `traefik.yml` (static config):

```yaml
experimental:
  localPlugins:
    my-plugin:
      moduleName: "github.com/your-org/my-plugin"
```

In `dynamic/middlewares.yml`:

```yaml
http:
  middlewares:
    my-custom-check:
      plugin:
        my-plugin:
          someConfigKey: someValue
```

## The Traefik Plugin Catalogue

Pre-built plugins are published at [plugins.traefik.io](https://plugins.traefik.io).
Register them in `traefik.yml` under `experimental.plugins` and reference by name.

Popular plugins:
- `traefik-real-ip` — extract real client IP behind other proxies
- `rewritebody` — regex-replace response bodies
- `jwt-middleware` — validate and decode JWTs at the proxy layer

## When NOT to Use Plugins

Plugins add complexity and a separate build/deploy cycle (Go + WASM).
Before reaching for a plugin, check if a built-in middleware solves the problem:
- Rate limiting → `rateLimit` middleware
- Header manipulation → `headers` middleware
- Redirect → `redirectScheme` or `redirectRegex`
- Auth → `basicAuth` or `forwardAuth`

Only write a plugin when no built-in middleware or combination of middleware can do the job.
