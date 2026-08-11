import { api, API_BASE } from './client'

export const CACHE_HUB_URL = `${API_BASE.replace(/\/api$/, '')}/hubs/cache`

export interface CommandCount {
  command: string
  count: number
}
export interface SlowCommand {
  command: string
  ms: number
  atUtc: string
}
export interface MetricsSnapshot {
  timestampUtc: string
  uptimeSeconds: number
  totalKeys: number
  expiredKeys: number
  connectedClients: number
  processMemoryBytes: number
  gcHeapBytes: number
  cpuPercent: number
  requestsPerSecond: number
  averageLatencyMs: number
  totalCommands: number
  hits: number
  misses: number
  hitRate: number
  missRate: number
  gen0Collections: number
  gen1Collections: number
  gen2Collections: number
  topCommands: CommandCount[]
  slowCommands: SlowCommand[]
}
export interface KeyInfo {
  key: string
  sizeBytes: number
  ttlSeconds: number
}
export interface PagedKeys {
  total: number
  page: number
  pageSize: number
  items: KeyInfo[]
}
export interface ClientInfo {
  id: string
  remoteEndpoint: string
  connectedAtUtc: string
  commandsProcessed: number
}
export interface CacheLogEntry {
  timestampUtc: string
  level: string
  category: string
  message: string
}
export interface HealthInfo {
  status: string
  uptimeSeconds: number
  keys: number
  clients: number
}

export const CacheApi = {
  // monitoring
  stats: () => api.get<MetricsSnapshot>('/cache/stats').then((r) => r.data),
  keys: (pattern: string | undefined, page: number, pageSize: number) =>
    api.get<PagedKeys>('/cache/keys', { params: { pattern, page, pageSize } }).then((r) => r.data),
  clients: () => api.get<ClientInfo[]>('/cache/clients').then((r) => r.data),
  config: () => api.get<Record<string, unknown>>('/cache/config').then((r) => r.data),
  health: () => api.get<HealthInfo>('/cache/health').then((r) => r.data),
  logs: (take = 100) => api.get<CacheLogEntry[]>('/cache/logs', { params: { take } }).then((r) => r.data),

  // commands (used by the Key Explorer)
  get: (key: string) =>
    api.get<{ ok: boolean; value?: string }>(`/cache/get/${encodeURIComponent(key)}`).then((r) => r.data),
  set: (key: string, value: string, ttlSeconds?: number) =>
    api.post('/cache/set', { key, value, ttlSeconds }).then((r) => r.data),
  del: (key: string) => api.delete(`/cache/del/${encodeURIComponent(key)}`).then((r) => r.data),
  expire: (key: string, ttlSeconds: number) => api.post('/cache/expire', { key, ttlSeconds }).then((r) => r.data),
  flush: () => api.post('/cache/flush').then((r) => r.data),
  save: () => api.post('/cache/save').then((r) => r.data),
}
