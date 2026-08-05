import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { WebsitesApi, type WebsiteListItem } from '../api/cicd'
import { StatusBadge } from './StatusBadge'
import { WebsiteWizard } from './WebsiteWizard'
import { DeployDialog } from './DeployDialog'
import { DeployConsole } from './DeployConsole'
import { DeploymentHistory } from './DeploymentHistory'
import './cicd.css'

export function WebsitesPage() {
  const { username, logout } = useAuth()
  const [sites, setSites] = useState<WebsiteListItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [wizard, setWizard] = useState<{ open: boolean; id: number | null }>({ open: false, id: null })
  const [deployFor, setDeployFor] = useState<WebsiteListItem | null>(null)
  const [historyFor, setHistoryFor] = useState<WebsiteListItem | null>(null)
  const [consoleId, setConsoleId] = useState<number | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    WebsitesApi.list()
      .then(setSites)
      .catch((e) => setError(e.response?.data?.message ?? 'Failed to load websites.'))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function onDelete(w: WebsiteListItem) {
    if (!confirm(`Delete "${w.websiteName}" and all its deployment history?`)) return
    await WebsitesApi.remove(w.websiteId)
    load()
  }

  return (
    <div className="cicd">
      <header className="topbar">
        <div className="brand-inline">
          <div className="brand-mark">CD</div>
          Deploy Portal
        </div>
        <nav className="nav">
          <Link to="/agencies">Agencies</Link>
          <Link to="/websites" className="active">
            Deployments
          </Link>
        </nav>
        <div style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
          <span className="muted small">{username}</span>
          <button className="btn btn-ghost sm" onClick={logout}>
            Sign out
          </button>
        </div>
      </header>

      <main className="content">
        <div className="page-head">
          <div>
            <h1>Websites</h1>
            <p className="muted small">{sites.length} configured</p>
          </div>
          <button className="btn btn-primary" onClick={() => setWizard({ open: true, id: null })}>
            + Create Website
          </button>
        </div>

        {error && <div className="alert alert-bad" style={{ marginBottom: '1rem' }}>{error}</div>}

        <div className="card table-card">
          <table className="tbl">
            <thead>
              <tr>
                <th>Website</th>
                <th>Repository</th>
                <th>Branch</th>
                <th>Last Deployment</th>
                <th className="right" style={{ textAlign: 'right' }}>
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={5} className="muted" style={{ textAlign: 'center' }}>
                    Loading…
                  </td>
                </tr>
              ) : sites.length === 0 ? (
                <tr>
                  <td colSpan={5} className="muted" style={{ textAlign: 'center' }}>
                    No websites yet. Click “Create Website”.
                  </td>
                </tr>
              ) : (
                sites.map((w) => (
                  <tr key={w.websiteId}>
                    <td>
                      <div className="cell-title">{w.websiteName}</div>
                      <div className="muted small">{w.projectType}</div>
                    </td>
                    <td className="mono small">{shortRepo(w.repositoryUrl)}</td>
                    <td className="mono">{w.defaultBranch ?? '—'}</td>
                    <td>
                      {w.lastDeployment ? (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.2rem' }}>
                          <StatusBadge status={w.lastDeployment.status} />
                          <span className="muted small">
                            {w.lastDeployment.finishedOn
                              ? new Date(w.lastDeployment.finishedOn).toLocaleString()
                              : 'in progress'}
                          </span>
                        </div>
                      ) : (
                        <span className="muted small">Never</span>
                      )}
                    </td>
                    <td>
                      <div className="row-actions">
                        <button className="btn btn-primary xs" onClick={() => setDeployFor(w)}>
                          Deploy
                        </button>
                        <button className="btn btn-ghost xs" onClick={() => setHistoryFor(w)}>
                          Logs
                        </button>
                        <button className="btn btn-ghost xs" onClick={() => setWizard({ open: true, id: w.websiteId })}>
                          Edit
                        </button>
                        <button className="btn btn-danger xs" onClick={() => onDelete(w)}>
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </main>

      {wizard.open && (
        <WebsiteWizard
          websiteId={wizard.id}
          onClose={() => setWizard({ open: false, id: null })}
          onSaved={() => {
            setWizard({ open: false, id: null })
            load()
          }}
        />
      )}

      {deployFor && (
        <DeployDialog
          website={deployFor}
          onClose={() => setDeployFor(null)}
          onStarted={(id) => {
            setDeployFor(null)
            setConsoleId(id)
            load()
          }}
        />
      )}

      {historyFor && (
        <DeploymentHistory
          website={historyFor}
          onClose={() => setHistoryFor(null)}
          onOpenConsole={(id) => {
            setHistoryFor(null)
            setConsoleId(id)
          }}
        />
      )}

      {consoleId !== null && (
        <DeployConsole deploymentId={consoleId} onClose={() => setConsoleId(null)} onChanged={load} />
      )}
    </div>
  )
}

function shortRepo(url?: string | null) {
  if (!url) return '—'
  return url.replace(/^https?:\/\/(www\.)?github\.com\//i, '').replace(/\.git$/, '')
}
