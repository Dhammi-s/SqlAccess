import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { CACHE_HUB_URL, CacheApi, type MetricsSnapshot } from '../api/cache'
import { getToken } from '../api/client'

const MAX_POINTS = 60

/** Live metrics via SignalR, plus rolling history arrays for the realtime charts. */
export function useCacheMetrics() {
  const [metrics, setMetrics] = useState<MetricsSnapshot | null>(null)
  const [connected, setConnected] = useState(false)
  const [history, setHistory] = useState<{ cpu: number[]; mem: number[]; ops: number[]; lat: number[] }>({
    cpu: [],
    mem: [],
    ops: [],
    lat: [],
  })
  const connRef = useRef<signalR.HubConnection | null>(null)

  useEffect(() => {
    let disposed = false

    // Seed immediately so the UI isn't blank before the first push.
    CacheApi.stats().then((s) => !disposed && apply(s)).catch(() => {})

    function apply(s: MetricsSnapshot) {
      setMetrics(s)
      setHistory((h) => ({
        cpu: [...h.cpu, s.cpuPercent].slice(-MAX_POINTS),
        mem: [...h.mem, Math.round(s.processMemoryBytes / 1024 / 1024)].slice(-MAX_POINTS),
        ops: [...h.ops, s.requestsPerSecond].slice(-MAX_POINTS),
        lat: [...h.lat, s.averageLatencyMs].slice(-MAX_POINTS),
      }))
    }

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(CACHE_HUB_URL, { accessTokenFactory: () => getToken() ?? '' })
      .withAutomaticReconnect()
      .build()
    connRef.current = conn
    conn.on('metrics', (s: MetricsSnapshot) => !disposed && apply(s))
    conn.onreconnected(() => setConnected(true))
    conn.onclose(() => setConnected(false))
    conn.start().then(() => !disposed && setConnected(true)).catch(() => {})

    return () => {
      disposed = true
      conn.stop()
    }
  }, [])

  return { metrics, history, connected }
}
