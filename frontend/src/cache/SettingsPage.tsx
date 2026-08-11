import { Fragment, useEffect, useState } from 'react'
import { CacheApi, type HealthInfo } from '../api/cache'
import { useAuth } from '../auth/AuthContext'

function labelize(key: string) {
  return key.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase())
}

export function SettingsPage() {
  const { role } = useAuth() as { role?: string }
  const isAdmin = !role || role === 'Admin' // single-login app defaults to full access
  const [config, setConfig] = useState<Record<string, unknown> | null>(null)
  const [health, setHealth] = useState<HealthInfo | null>(null)
  const [busy, setBusy] = useState('')
  const [msg, setMsg] = useState<string | null>(null)

  function loadHealth() {
    CacheApi.health().then(setHealth)
  }
  useEffect(() => {
    CacheApi.config().then(setConfig)
    loadHealth()
    const t = setInterval(loadHealth, 4000)
    return () => clearInterval(t)
  }, [])

  async function save() {
    setBusy('save')
    try {
      await CacheApi.save()
      setMsg('Snapshot written to disk.')
    } catch {
      setMsg('Snapshot failed.')
    } finally {
      setBusy('')
    }
  }
  async function flush() {
    if (!confirm('Flush ALL keys from the cache? This cannot be undone.')) return
    setBusy('flush')
    try {
      await CacheApi.flush()
      setMsg('Cache flushed.')
      loadHealth()
    } catch {
      setMsg('Flush failed.')
    } finally {
      setBusy('')
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Settings</h1>
          <p className="muted small">Server configuration &amp; maintenance</p>
        </div>
        {health && (
          <span className={`badge ${health.status === 'Healthy' ? 'Success' : 'Failed'}`}>
            <span className="dot" />
            {health.status}
          </span>
        )}
      </div>

      {msg && (
        <div className="banner info" onClick={() => setMsg(null)}>
          {msg}
        </div>
      )}

      <div className="settings-grid">
        <div className="card pad">
          <h3 className="card-h">Server Status</h3>
          {health ? (
            <dl className="kv">
              <dt>Status</dt>
              <dd>{health.status}</dd>
              <dt>Uptime</dt>
              <dd>{Math.floor(health.uptimeSeconds)}s</dd>
              <dt>Keys</dt>
              <dd>{health.keys.toLocaleString()}</dd>
              <dt>Clients</dt>
              <dd>{health.clients}</dd>
            </dl>
          ) : (
            <p className="muted">Loading…</p>
          )}
        </div>

        <div className="card pad">
          <h3 className="card-h">Configuration</h3>
          {config ? (
            <dl className="kv">
              {Object.entries(config).map(([k, v]) => (
                <Fragment key={k}>
                  <dt>{labelize(k)}</dt>
                  <dd className="mono">{String(v)}</dd>
                </Fragment>
              ))}
            </dl>
          ) : (
            <p className="muted">Loading…</p>
          )}
          <p className="muted small" style={{ marginTop: '0.75rem' }}>
            Configuration is applied at startup from <code>appsettings.json</code> (<code>Cache</code> section).
          </p>
        </div>

        <div className="card pad">
          <h3 className="card-h">Maintenance</h3>
          {isAdmin ? (
            <>
              <p className="muted small">Persistence and data operations.</p>
              <div className="btn-row">
                <button className="btn btn-primary" onClick={save} disabled={busy === 'save'}>
                  {busy === 'save' ? 'Saving…' : 'Save snapshot now'}
                </button>
                <button className="btn btn-danger" onClick={flush} disabled={busy === 'flush'}>
                  {busy === 'flush' ? 'Flushing…' : 'Flush all keys'}
                </button>
              </div>
            </>
          ) : (
            <p className="muted">Maintenance actions require the Admin role.</p>
          )}
        </div>
      </div>
    </>
  )
}
