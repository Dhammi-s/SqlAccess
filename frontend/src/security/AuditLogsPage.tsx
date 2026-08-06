import { useEffect, useState } from 'react'
import { VaultApi, type AuditLogItem } from '../api/vault'

export function AuditLogsPage() {
  const [logs, setLogs] = useState<AuditLogItem[]>([])
  const [loading, setLoading] = useState(true)
  const [onlyFailures, setOnlyFailures] = useState(false)

  function load() {
    setLoading(true)
    VaultApi.auditLogs(300)
      .then(setLogs)
      .finally(() => setLoading(false))
  }
  useEffect(load, [])

  const rows = onlyFailures ? logs.filter((l) => !l.success) : logs

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Audit Logs</h1>
          <p className="muted small">{rows.length} events</p>
        </div>
        <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
          <label className="switch-row">
            <input type="checkbox" checked={onlyFailures} onChange={(e) => setOnlyFailures(e.target.checked)} />
            Failures only
          </label>
          <button className="btn btn-ghost sm" onClick={load}>
            Refresh
          </button>
        </div>
      </div>

      <div className="card table-card">
        <table className="tbl">
          <thead>
            <tr>
              <th>When</th>
              <th>Action</th>
              <th>Application</th>
              <th>Secret</th>
              <th>IP Address</th>
              <th>Result</th>
              <th>Detail</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={7} className="muted" style={{ textAlign: 'center' }}>
                  Loading…
                </td>
              </tr>
            ) : rows.length === 0 ? (
              <tr>
                <td colSpan={7} className="muted" style={{ textAlign: 'center' }}>
                  No audit events.
                </td>
              </tr>
            ) : (
              rows.map((l) => (
                <tr key={l.auditLogId}>
                  <td className="small">{new Date(l.timestamp).toLocaleString()}</td>
                  <td className="mono small">{l.action}</td>
                  <td className="small">{l.applicationName ?? '—'}</td>
                  <td className="small">{l.secretName ?? '—'}</td>
                  <td className="mono small">{l.ipAddress ?? '—'}</td>
                  <td>
                    <span className={`badge ${l.success ? 'Success' : 'Failed'}`}>
                      <span className="dot" />
                      {l.success ? 'Success' : 'Failure'}
                    </span>
                  </td>
                  <td className="muted small">{l.detail ?? '—'}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </>
  )
}
