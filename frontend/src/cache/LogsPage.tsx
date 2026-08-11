import { useEffect, useState } from 'react'
import { CacheApi, type CacheLogEntry } from '../api/cache'

const LEVEL_CLASS: Record<string, string> = {
  Information: 'lvl-info',
  Warning: 'lvl-warn',
  Error: 'lvl-err',
  Debug: 'lvl-dbg',
}

export function LogsPage() {
  const [logs, setLogs] = useState<CacheLogEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [auto, setAuto] = useState(true)

  function load() {
    CacheApi.logs()
      .then(setLogs)
      .finally(() => setLoading(false))
  }
  useEffect(() => {
    load()
    if (!auto) return
    const t = setInterval(load, 2000)
    return () => clearInterval(t)
  }, [auto])

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Logs</h1>
          <p className="muted small">{logs.length} recent entries</p>
        </div>
        <label className="chk">
          <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} /> Auto-refresh
        </label>
      </div>

      <div className="card log-card">
        {loading ? (
          <div className="muted" style={{ padding: '1rem' }}>
            Loading…
          </div>
        ) : logs.length === 0 ? (
          <div className="muted" style={{ padding: '1rem' }}>
            No log entries yet.
          </div>
        ) : (
          <div className="log-list">
            {logs.map((l, i) => (
              <div className="log-row" key={i}>
                <span className="log-time">{new Date(l.timestampUtc).toLocaleTimeString()}</span>
                <span className={`log-level ${LEVEL_CLASS[l.level] ?? 'lvl-info'}`}>{l.level}</span>
                <span className="log-msg">{l.message}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </>
  )
}
