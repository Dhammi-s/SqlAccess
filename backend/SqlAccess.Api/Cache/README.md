# In-Memory Cache — a Redis-like data store

A production-grade, Redis-inspired in-memory key/value store built **inside** the
`SqlAccess.Api` application (no separate service or process). It provides a thread-safe
store, a RESP wire protocol over TCP, disk persistence with crash recovery, live
monitoring, a SignalR metrics stream, and a React dashboard.

The whole feature lives under `Cache/` in the API project and a `cache/` folder in the
React frontend. It shares the app's Kestrel host, DI container, configuration, and JWT
auth — it is a module, not a microservice.

---

## Contents

- [Architecture](#architecture)
- [Supported commands](#supported-commands)
- [TCP / RESP protocol](#tcp--resp-protocol)
- [REST API](#rest-api)
- [Persistence & recovery](#persistence--recovery)
- [Monitoring & SignalR](#monitoring--signalr)
- [React dashboard](#react-dashboard)
- [Configuration](#configuration)
- [Running it](#running-it)
- [Testing](#testing)

---

## Architecture

```
                        ┌──────────────────────────────────────────┐
   redis-cli / nc  ───► │  CacheTcpServer  (TCP :6380, RESP)        │
                        │    └─ RespConnection ─ CommandExecutor    │
                        └──────────────┬───────────────────────────┘
   HTTP clients ──►  CacheController ──┤
   (REST facade)                       │
                                       ▼
                             ┌───────────────────┐     ┌──────────────────┐
                             │  InMemoryCacheStore│◄───►│  ICachePersistence│
                             │  (ConcurrentDict)  │     │  File / Null      │
                             └─────────┬──────────┘     └──────────────────┘
                                       │ RecordCommand
                                       ▼
   React dashboard ◄─ SignalR ◄─ MonitoringService ◄─ CacheMonitoringController (REST)
        (/hubs/cache, 1 Hz)
```

### Layers

| Folder            | Responsibility |
|-------------------|----------------|
| `Domain/`         | `CacheEntry` — value + optional absolute expiry. |
| `Interfaces/`     | `ICacheStore`, `ICachePersistence` — the two seams the rest of the system depends on. |
| `Services/`       | `InMemoryCacheStore` — the engine (a `ConcurrentDictionary` with `Interlocked` counters). |
| `Persistence/`    | `FilePersistence` (AOF + snapshot) and `NullPersistence` (memory-only). |
| `Networking/`     | `RespValue` (wire types), `RespConnection` (parser/writer), `CommandExecutor` (dispatch), `CacheTcpServer` (listener), `ConnectionManager` (client registry). |
| `Monitoring/`     | `MonitoringService` + metric models — throughput, latency, memory/CPU/GC, hit rate, top/slow commands. |
| `Hubs/`           | `CacheMetricsHub` — SignalR hub for live metrics. |
| `Workers/`        | `CacheCleanupWorker` (active expiry), `PersistenceWorker` (periodic snapshots), `MetricsBroadcastWorker` (1 Hz push). |
| `Controllers/`    | `CacheController` (command REST facade), `CacheMonitoringController` (metrics REST). |

### Design principles

- **SOLID / DI-first.** Every collaborator is an interface (`ICacheStore`,
  `ICachePersistence`, `IMonitoringService`, `IConnectionManager`) registered as a
  singleton. Swapping persistence to memory-only is a one-line config change.
- **Thread-safe by construction.** The store uses a `ConcurrentDictionary` and
  `Interlocked` counters; INCR/DECR are atomic via `AddOrUpdate`.
- **Lazy + active expiry.** Keys expire on access (lazy) and are also swept by a
  background worker (active), so memory is reclaimed even for keys never read again.
- **Synchronous store, async I/O.** Store operations are pure in-memory and synchronous
  (adding `async` would only add allocation/latency). Networking and persistence I/O are
  async.

---

## Supported commands

| Command | Arguments | Reply | Notes |
|---------|-----------|-------|-------|
| `PING` | `[message]` | `+PONG` or bulk echo | Liveness. |
| `SET` | `key value [EX secs \| PX ms]` | `+OK` | Overwrites; optional TTL. |
| `GET` | `key` | bulk or nil | Nil (`$-1`) on miss/expired. |
| `DEL` | `key [key …]` | integer | Count of live keys removed. |
| `EXISTS` | `key [key …]` | integer | Count present. |
| `TTL` | `key` | integer | `-2` no key, `-1` no expiry, else seconds. |
| `EXPIRE` | `key seconds` | `1`/`0` | `0` if the key is absent. |
| `INCR` / `DECR` | `key` | integer | Missing key starts at 0; errors on non-integer. |
| `INCRBY` / `DECRBY` | `key amount` | integer | |
| `DBSIZE` | — | integer | Key count. |
| `FLUSH` / `FLUSHALL` / `FLUSHDB` | — | `+OK` | Removes all keys. |
| `QUIT` | — | `+OK` | Server closes the connection. |
| `COMMAND` | — | empty array | Stub so `redis-cli` connects cleanly. |

Unknown commands and arity errors return a RESP error (`-ERR …`) rather than throwing.

---

## TCP / RESP protocol

The TCP server (`CacheTcpServer`) listens on `127.0.0.1:6380` by default and speaks the
classic Redis RESP wire format, so **`redis-cli` and standard Redis client libraries work
unchanged**:

```bash
redis-cli -p 6380 SET greeting "hello world"
redis-cli -p 6380 GET greeting
```

For quick manual testing it also accepts **inline commands** (space-separated), so plain
`nc`/telnet works:

```
$ nc 127.0.0.1 6380
PING
+PONG
SET k hello
+OK
GET k
$5
hello
```

Wire types encoded by `RespValue`: `+` simple string, `-` error, `:` integer,
`$` bulk string (`$-1` = nil), `*` array. Bulk-string lengths are UTF-8 **byte** counts.

> The TCP protocol is unauthenticated, which is why the default bind address is loopback
> only. Do not expose port 6380 publicly.

---

## REST API

All routes are under `api/cache` and require the app's JWT (`[Authorize]`). The REST
facade is convenient for the dashboard and for clients that can't open a raw socket.

### Commands — `CacheController`

| Method & path | Body / params | Purpose |
|---------------|---------------|---------|
| `GET  /api/cache/ping` | — | Liveness. |
| `POST /api/cache/set` | `{ key, value, ttlSeconds? }` | SET. |
| `GET  /api/cache/get/{key}` | — | GET. |
| `DELETE /api/cache/del/{key}` | — | DEL. |
| `GET  /api/cache/exists/{key}` | — | EXISTS. |
| `GET  /api/cache/ttl/{key}` | — | TTL. |
| `POST /api/cache/expire` | `{ key, ttlSeconds }` | EXPIRE. |
| `POST /api/cache/incr/{key}` | — | INCR. |
| `POST /api/cache/decr/{key}` | — | DECR. |
| `POST /api/cache/flush` | — | FLUSH all. |
| `POST /api/cache/save` | — | Force a snapshot to disk. |

### Monitoring — `CacheMonitoringController`

| Method & path | Purpose |
|---------------|---------|
| `GET /api/cache/stats` | Full `MetricsSnapshot` (keys, rps, latency, memory/CPU/GC, hit rate, top/slow commands). |
| `GET /api/cache/keys?pattern=&page=&pageSize=` | Paged, filterable key list. |
| `GET /api/cache/clients` | Connected TCP clients. |
| `GET /api/cache/config` | Effective `CacheOptions`. |
| `GET /api/cache/health` | Status, uptime, key/client counts. |
| `GET /api/cache/logs?take=` | Recent in-memory event log. |

---

## Persistence & recovery

Selected by `Cache:PersistenceMode`:

- **`None`** — `NullPersistence`, memory-only (fastest; data lost on restart).
- **`Aof`** — append-only log of every mutation.
- **`Snapshot`** — periodic full point-in-time dumps.
- **`Both`** (default) — AOF for durability between snapshots, snapshots to bound AOF size.

Files live in `Cache:DataDirectory` (default `App_Data/cache`, git-ignored):

- `appendonly.aof` — one line per mutation. Keys/values are Base64 so they never contain
  spaces:
  ```
  S <b64key> <b64value> <expiryTicksUtc|->   # SET
  D <b64key>                                 # DEL
  E <b64key> <expiryTicksUtc>                # EXPIRE
  F                                          # FLUSH
  ```
- `snapshot.rdb` — a full dump of live entries (same `S` line format).

**Recovery** (runs at startup, before serving): the snapshot is loaded, then the AOF is
replayed on top — folded into a working set first so `DEL`/`FLUSH` apply cleanly — and the
result is loaded into the store. Already-expired entries are skipped. Taking a snapshot
truncates the AOF so it only ever holds post-snapshot writes. On graceful shutdown the
persistence worker writes a final snapshot and flushes.

---

## Monitoring & SignalR

`MonitoringService` is the single source of truth for runtime metrics:

- **Throughput / latency** — every executed command reports its name and elapsed time via
  `RecordCommand`. Requests-per-second and average latency are computed as deltas per
  snapshot.
- **Process** — working-set memory, CPU% (delta of `Process.TotalProcessorTime`), GC
  collection counts, uptime.
- **Store** — key count, expired count, hit/miss and hit rate.
- **Top & slow commands** — the five most-frequent commands and any command over the slow
  threshold (5 ms).
- **Event log** — a bounded ring buffer (last 500 entries) surfaced by `/logs`.

`MetricsBroadcastWorker` pushes a `MetricsSnapshot` to the `CacheMetricsHub` every second;
clients subscribe on `metrics`. The hub is mapped at **`/hubs/cache`** and is
`[Authorize]`-protected (the React client supplies the JWT via `accessTokenFactory`).

---

## React dashboard

Under `frontend/src/cache/`, reachable at **`/cache`** (a "Cache" link is in the top nav of
every authenticated page). Tabs:

- **Dashboard** — live stat tiles (keys, ops/sec, hit/miss, latency, clients, memory, CPU,
  uptime) plus four realtime SVG charts and the top/slow-command tables, all driven by the
  SignalR stream (`useCacheMetrics`).
- **Key Explorer** — search, pagination, value viewer (auto-pretty-prints JSON), set TTL,
  delete.
- **Clients** — connected TCP clients, auto-refreshing.
- **Logs** — the rolling event log with auto-refresh.
- **Settings** — server status, effective configuration, and maintenance actions (save
  snapshot / flush).

The charts are dependency-free (hand-rolled SVG in `RealtimeChart.tsx`); the only added
dependency is `@microsoft/signalr`. Styling reuses the app's existing `.cicd` theme plus a
small `cache.css`.

---

## Configuration

Bound from the `Cache` section of `appsettings.json` (`CacheOptions`):

```jsonc
{
  "Cache": {
    "CleanupIntervalSeconds": 15,      // active-expiry sweep interval
    "TcpEnabled": true,
    "TcpPort": 6380,
    "TcpBindAddress": "127.0.0.1",     // loopback — the TCP protocol is unauthenticated
    "PersistenceMode": "Both",         // None | Aof | Snapshot | Both
    "DataDirectory": "App_Data/cache", // relative paths resolve under the content root
    "SnapshotIntervalSeconds": 300
  }
}
```

---

## Running it

The cache starts automatically with the API — no extra steps:

```bash
cd backend/SqlAccess.Api
dotnet run
```

On startup you'll see recovery and the TCP listener come up:

```
Cache recovered N keys from disk.
Cache TCP server listening on 127.0.0.1:6380 (RESP).
```

Exercise it over TCP with `redis-cli -p 6380 …`, over REST at `/api/cache/*` (with a JWT),
or through the dashboard at `/cache`.

---

## Testing

Unit and integration tests live in `backend/SqlAccess.Tests` (xUnit) and are wired into
`backend/SqlAccess.slnx`.

```bash
cd backend
dotnet test
```

Coverage (63 tests):

- **`CacheStoreTests`** — every store operation, expiry semantics, atomic concurrent
  increments, stats.
- **`CommandExecutorTests`** — RESP command dispatch asserted on the exact reply bytes,
  including error and arity cases and case-insensitivity.
- **`RespProtocolTests`** — `RespValue` serialization and `RespConnection` parsing of both
  the array and inline command forms.
- **`FilePersistenceTests`** — AOF append, snapshot + truncation, and recovery folding
  (S/D/E/F, expired-skip, snapshot-then-AOF precedence) — the exact restart sequence.

> The cache engine itself lives inside `SqlAccess.Api`, as required. `SqlAccess.Tests` is a
> separate project only because xUnit test assemblies cannot ship inside a production web
> app — it references the API and adds no runtime dependency to it.
