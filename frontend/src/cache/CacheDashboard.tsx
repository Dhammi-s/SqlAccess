import { useCacheMetrics } from './useCacheMetrics'
import { RealtimeChart } from './RealtimeChart'

function fmtBytes(b: number) {
  if (b > 1024 * 1024 * 1024) return (b / 1024 / 1024 / 1024).toFixed(2) + ' GB'
  return (b / 1024 / 1024).toFixed(1) + ' MB'
}
function fmtUptime(s: number) {
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const sec = Math.floor(s % 60)
  return `${h}h ${m}m ${sec}s`
}

function Stat({ label, value, sub, accent }: { label: string; value: string; sub?: string; accent?: string }) {
  return (
    <div className="stat-tile">
      <div className="stat-label">{label}</div>
      <div className="stat-value" style={accent ? { color: accent } : undefined}>
        {value}
      </div>
      {sub && <div className="stat-sub">{sub}</div>}
    </div>
  )
}

export function CacheDashboard() {
  const { metrics: m, history, connected } = useCacheMetrics()

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Dashboard</h1>
          <p className="muted small">
            <span className={`live-dot ${connected ? 'on' : 'off'}`} /> {connected ? 'Live' : 'Connecting…'} · updates every second
          </p>
        </div>
        <span className={`badge ${m ? 'Success' : 'Queued'}`}>
          <span className="dot" />
          {m ? 'Healthy' : '—'}
        </span>
      </div>

      <div className="stat-grid">
        <Stat label="Keys" value={m ? m.totalKeys.toLocaleString() : '—'} sub={m ? `${m.expiredKeys} expired` : ''} />
        <Stat label="Ops / sec" value={m ? m.requestsPerSecond.toString() : '—'} accent="#2563eb" />
        <Stat label="Hit ratio" value={m ? `${m.hitRate}%` : '—'} accent="#16a34a" sub={m ? `${m.hits} hits` : ''} />
        <Stat label="Miss ratio" value={m ? `${m.missRate}%` : '—'} accent="#dc2626" sub={m ? `${m.misses} misses` : ''} />
        <Stat label="Avg latency" value={m ? `${m.averageLatencyMs} ms` : '—'} />
        <Stat label="Clients" value={m ? m.connectedClients.toString() : '—'} />
        <Stat label="Memory" value={m ? fmtBytes(m.processMemoryBytes) : '—'} sub={m ? `GC heap ${fmtBytes(m.gcHeapBytes)}` : ''} />
        <Stat label="CPU" value={m ? `${m.cpuPercent}%` : '—'} />
        <Stat label="Uptime" value={m ? fmtUptime(m.uptimeSeconds) : '—'} />
        <Stat label="Total commands" value={m ? m.totalCommands.toLocaleString() : '—'} />
      </div>

      <div className="chart-grid">
        <div className="card chart-card">
          <div className="chart-title">Operations / sec</div>
          <RealtimeChart data={history.ops} color="#2563eb" />
        </div>
        <div className="card chart-card">
          <div className="chart-title">CPU %</div>
          <RealtimeChart data={history.cpu} color="#7c3aed" suffix="%" />
        </div>
        <div className="card chart-card">
          <div className="chart-title">Memory (MB)</div>
          <RealtimeChart data={history.mem} color="#16a34a" />
        </div>
        <div className="card chart-card">
          <div className="chart-title">Avg latency (ms)</div>
          <RealtimeChart data={history.lat} color="#d97706" />
        </div>
      </div>

      <div className="chart-grid">
        <div className="card table-card">
          <div className="chart-title" style={{ padding: '0.8rem 1rem 0' }}>
            Top commands
          </div>
          <table className="tbl">
            <thead>
              <tr>
                <th>Command</th>
                <th className="right" style={{ textAlign: 'right' }}>
                  Count
                </th>
              </tr>
            </thead>
            <tbody>
              {(m?.topCommands ?? []).length === 0 ? (
                <tr>
                  <td colSpan={2} className="muted" style={{ textAlign: 'center' }}>
                    No commands yet.
                  </td>
                </tr>
              ) : (
                m!.topCommands.map((c) => (
                  <tr key={c.command}>
                    <td className="mono">{c.command}</td>
                    <td className="right" style={{ textAlign: 'right' }}>
                      {c.count.toLocaleString()}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <div className="card table-card">
          <div className="chart-title" style={{ padding: '0.8rem 1rem 0' }}>
            Slow commands ({'>'}5ms)
          </div>
          <table className="tbl">
            <thead>
              <tr>
                <th>Command</th>
                <th>ms</th>
                <th>When</th>
              </tr>
            </thead>
            <tbody>
              {(m?.slowCommands ?? []).length === 0 ? (
                <tr>
                  <td colSpan={3} className="muted" style={{ textAlign: 'center' }}>
                    None — all fast.
                  </td>
                </tr>
              ) : (
                m!.slowCommands.map((c, i) => (
                  <tr key={i}>
                    <td className="mono">{c.command}</td>
                    <td>{c.ms}</td>
                    <td className="small">{new Date(c.atUtc).toLocaleTimeString()}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </>
  )
}
