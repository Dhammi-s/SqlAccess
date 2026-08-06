import { useEffect, useState } from 'react'
import {
  VaultApi,
  type AppListItem,
  type ApplicationSecretItem,
  type SecretListItem,
} from '../api/vault'

export function SecretAccessPage() {
  const [apps, setApps] = useState<AppListItem[]>([])
  const [secrets, setSecrets] = useState<SecretListItem[]>([])
  const [assignments, setAssignments] = useState<ApplicationSecretItem[]>([])
  const [appId, setAppId] = useState<number | ''>('')
  const [secretId, setSecretId] = useState<number | ''>('')
  const [busy, setBusy] = useState(false)
  const [flash, setFlash] = useState<string | null>(null)

  function loadAll() {
    VaultApi.listApplications().then(setApps)
    VaultApi.listSecrets().then(setSecrets)
    VaultApi.listAssignments().then(setAssignments)
  }
  useEffect(loadAll, [])

  function showFlash(m: string) {
    setFlash(m)
    setTimeout(() => setFlash(null), 3000)
  }

  async function assign() {
    if (appId === '' || secretId === '') return
    setBusy(true)
    try {
      await VaultApi.assignSecret(Number(appId), Number(secretId))
      showFlash('Secret assigned.')
      VaultApi.listAssignments().then(setAssignments)
    } finally {
      setBusy(false)
    }
  }

  async function revoke(a: ApplicationSecretItem) {
    if (!confirm(`Revoke "${a.secretName}" from "${a.applicationName}"?`)) return
    await VaultApi.revoke(a.applicationSecretId)
    showFlash('Access revoked.')
    VaultApi.listAssignments().then(setAssignments)
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Secret Access</h1>
          <p className="muted small">Grant applications access to specific secrets</p>
        </div>
      </div>

      <div className="card" style={{ padding: '1.1rem', marginBottom: '1.25rem' }}>
        <div style={{ display: 'flex', gap: '0.9rem', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <label style={{ flex: 1, minWidth: 200 }}>
            Application
            <select value={appId} onChange={(e) => setAppId(e.target.value ? Number(e.target.value) : '')}>
              <option value="">Select application…</option>
              {apps.map((a) => (
                <option key={a.applicationId} value={a.applicationId}>
                  {a.name}
                </option>
              ))}
            </select>
          </label>
          <label style={{ flex: 1, minWidth: 200 }}>
            Secret
            <select value={secretId} onChange={(e) => setSecretId(e.target.value ? Number(e.target.value) : '')}>
              <option value="">Select secret…</option>
              {secrets.map((s) => (
                <option key={s.secretId} value={s.secretId}>
                  {s.name} ({s.secretType})
                </option>
              ))}
            </select>
          </label>
          <button className="btn btn-primary" onClick={assign} disabled={busy || appId === '' || secretId === ''}>
            {busy ? 'Assigning…' : 'Assign'}
          </button>
        </div>
      </div>

      {flash && <div className="alert alert-ok" style={{ marginBottom: '1rem' }}>{flash}</div>}

      <div className="card table-card">
        <table className="tbl">
          <thead>
            <tr>
              <th>Application</th>
              <th>Secret</th>
              <th>Granted</th>
              <th className="right" style={{ textAlign: 'right' }}>
                Actions
              </th>
            </tr>
          </thead>
          <tbody>
            {assignments.length === 0 ? (
              <tr>
                <td colSpan={4} className="muted" style={{ textAlign: 'center' }}>
                  No access grants yet.
                </td>
              </tr>
            ) : (
              assignments.map((a) => (
                <tr key={a.applicationSecretId}>
                  <td className="cell-title">{a.applicationName}</td>
                  <td>{a.secretName}</td>
                  <td className="small">{new Date(a.createdOn).toLocaleString()}</td>
                  <td className="right">
                    <div className="row-actions">
                      <button className="btn btn-danger xs" onClick={() => revoke(a)}>
                        Revoke
                      </button>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </>
  )
}
