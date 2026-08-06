import { useEffect, useState } from 'react'
import { VaultApi, type AppListItem, type RegisterAppResponse } from '../api/vault'

function copy(text: string) {
  navigator.clipboard?.writeText(text)
}

export function ApplicationsPage() {
  const [apps, setApps] = useState<AppListItem[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [created, setCreated] = useState<RegisterAppResponse | null>(null)

  function load() {
    setLoading(true)
    VaultApi.listApplications()
      .then(setApps)
      .finally(() => setLoading(false))
  }
  useEffect(load, [])

  async function register() {
    setBusy(true)
    try {
      const res = await VaultApi.registerApplication(name.trim())
      setCreated(res)
      setShowForm(false)
      setName('')
      load()
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Applications</h1>
          <p className="muted small">{apps.length} registered</p>
        </div>
        <button className="btn btn-primary" onClick={() => setShowForm(true)}>
          + Register Application
        </button>
      </div>

      <div className="card table-card">
        <table className="tbl">
          <thead>
            <tr>
              <th>Application</th>
              <th>Client ID</th>
              <th className="right" style={{ textAlign: 'right' }}>
                Secrets
              </th>
              <th>Status</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center' }}>
                  Loading…
                </td>
              </tr>
            ) : apps.length === 0 ? (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center' }}>
                  No applications yet.
                </td>
              </tr>
            ) : (
              apps.map((a) => (
                <tr key={a.applicationId}>
                  <td className="cell-title">{a.name}</td>
                  <td>
                    <div className="cred-box" style={{ maxWidth: 380 }}>
                      <span className="val mono">{a.clientId}</span>
                      <button className="btn btn-ghost xs" onClick={() => copy(a.clientId)}>
                        Copy
                      </button>
                    </div>
                  </td>
                  <td className="right" style={{ textAlign: 'right' }}>
                    {a.secretCount}
                  </td>
                  <td>
                    <span className={`badge ${a.isActive ? 'Success' : 'Cancelled'}`}>
                      <span className="dot" />
                      {a.isActive ? 'Active' : 'Disabled'}
                    </span>
                  </td>
                  <td className="small">{new Date(a.createdOn).toLocaleString()}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Register modal */}
      {showForm && (
        <div className="cicd-modal-backdrop" onMouseDown={() => setShowForm(false)}>
          <div className="cicd-modal" onMouseDown={(e) => e.stopPropagation()}>
            <div className="cicd-modal-head">
              <h2>Register application</h2>
              <button className="icon-btn" onClick={() => setShowForm(false)}>
                ✕
              </button>
            </div>
            <div className="fields">
              <label>
                Application name
                <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Billing Service" />
              </label>
            </div>
            <div className="cicd-modal-actions">
              <button className="btn btn-ghost" onClick={() => setShowForm(false)}>
                Cancel
              </button>
              <button className="btn btn-primary" onClick={register} disabled={busy || !name.trim()}>
                {busy ? 'Creating…' : 'Generate credentials'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* One-time credentials modal */}
      {created && (
        <div className="cicd-modal-backdrop" onMouseDown={() => setCreated(null)}>
          <div className="cicd-modal" onMouseDown={(e) => e.stopPropagation()}>
            <div className="cicd-modal-head">
              <h2>Credentials for “{created.name}”</h2>
              <button className="icon-btn" onClick={() => setCreated(null)}>
                ✕
              </button>
            </div>

            <div className="cred-label">Client ID</div>
            <div className="cred-box">
              <span className="val">{created.clientId}</span>
              <button className="btn btn-ghost xs" onClick={() => copy(created.clientId)}>
                Copy
              </button>
            </div>

            <div className="cred-label" style={{ marginTop: '0.9rem' }}>
              Client Secret
            </div>
            <div className="cred-box">
              <span className="val">{created.clientSecret}</span>
              <button className="btn btn-ghost xs" onClick={() => copy(created.clientSecret)}>
                Copy
              </button>
            </div>

            <div className="warn-note">
              ⚠ Copy the Client Secret now — it is shown <b>once</b> and never stored in plaintext. If lost, you must
              re-register the application.
            </div>

            <div className="cicd-modal-actions">
              <span />
              <button className="btn btn-primary" onClick={() => setCreated(null)}>
                I’ve saved it
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
