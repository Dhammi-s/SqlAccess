import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AgenciesApi, type AgencyListItem } from '../api/agencies'
import { useAuth } from '../auth/AuthContext'
import { AgencyFormModal } from './AgencyFormModal'
import { AgencyDetailsModal } from './AgencyDetailsModal'
import { DeployModal } from './DeployModal'

export function AgenciesPage() {
  const { username, logout } = useAuth()
  const [rows, setRows] = useState<AgencyListItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [includeArchived, setIncludeArchived] = useState(false)
  const [search, setSearch] = useState('')
  const [modal, setModal] = useState<{ open: boolean; id: number | null }>({ open: false, id: null })
  const [detailsId, setDetailsId] = useState<number | null>(null)
  const [deployOpen, setDeployOpen] = useState(false)
  const [testingId, setTestingId] = useState<number | null>(null)
  const [flash, setFlash] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    AgenciesApi.list(includeArchived)
      .then(setRows)
      .catch((err) => setError(err.response?.data?.message ?? 'Failed to load agencies.'))
      .finally(() => setLoading(false))
  }, [includeArchived])

  useEffect(() => {
    load()
  }, [load])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return rows
    return rows.filter((r) =>
      [r.agencyName, r.location, r.dbServer, r.dbName, r.dbUser]
        .filter(Boolean)
        .some((v) => v!.toLowerCase().includes(q)),
    )
  }, [rows, search])

  function showFlash(msg: string) {
    setFlash(msg)
    setTimeout(() => setFlash(null), 3500)
  }

  async function onArchive(row: AgencyListItem) {
    const restoring = row.isArchived
    if (!restoring && !confirm(`Archive "${row.agencyName}"? It will be hidden from the active list.`)) return
    try {
      await AgenciesApi.archive(row.agencyId, !restoring)
      showFlash(restoring ? 'Agency restored.' : 'Agency archived.')
      load()
    } catch {
      showFlash('Action failed.')
    }
  }

  async function onTest(row: AgencyListItem) {
    setTestingId(row.agencyId)
    try {
      const res = await AgenciesApi.test(row.agencyId)
      showFlash(`${row.agencyName}: ${res.success ? '✓ connected' : '✕ ' + res.message} (${res.elapsedMs} ms)`)
    } catch {
      showFlash('Connection test failed.')
    } finally {
      setTestingId(null)
    }
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand-inline">
          <div className="brand-mark sm">SA</div>
          <span>SQL Access</span>
        </div>
        <div className="topbar-right">
          <Link to="/websites" className="btn btn-ghost sm" style={{ textDecoration: 'none' }}>
            Deploy Portal →
          </Link>
          <span className="muted">{username}</span>
          <button className="btn btn-ghost sm" onClick={logout}>
            Sign out
          </button>
        </div>
      </header>

      <main className="content">
        <div className="page-head">
          <div>
            <h1>Agencies</h1>
            <p className="muted">{filtered.length} shown</p>
          </div>
          <div className="head-actions">
            <button className="btn btn-ghost" onClick={() => setDeployOpen(true)}>
              Deploy schema
            </button>
            <button className="btn btn-primary" onClick={() => setModal({ open: true, id: null })}>
              + Add agency
            </button>
          </div>
        </div>

        <div className="toolbar">
          <input
            className="search"
            placeholder="Search name, server, db, user…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <label className="switch-row">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.target.checked)}
            />
            Show archived
          </label>
        </div>

        {flash && <div className="alert alert-info">{flash}</div>}
        {error && <div className="alert alert-error">{error}</div>}

        <div className="card table-card">
          <table className="table">
            <thead>
              <tr>
                <th>Agency</th>
                <th>Location</th>
                <th>DB Server</th>
                <th>DB Name</th>
                <th>DB User</th>
                <th>Password</th>
                <th>Status</th>
                <th className="right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={8} className="muted center">
                    Loading…
                  </td>
                </tr>
              ) : filtered.length === 0 ? (
                <tr>
                  <td colSpan={8} className="muted center">
                    No agencies found.
                  </td>
                </tr>
              ) : (
                filtered.map((r) => (
                  <tr key={r.agencyId} className={r.isArchived ? 'archived' : ''}>
                    <td>
                      <button className="link-btn cell-title" onClick={() => setDetailsId(r.agencyId)}>
                        {r.agencyName}
                      </button>
                      {r.domainUrl && <div className="muted small">{r.domainUrl}</div>}
                    </td>
                    <td>{r.location ?? '—'}</td>
                    <td className="mono">{r.dbServer ?? '—'}</td>
                    <td className="mono">{r.dbName ?? '—'}</td>
                    <td className="mono">{r.dbUser ?? '—'}</td>
                    <td className="mono">{r.passwordMasked || '—'}</td>
                    <td>
                      <span className={`badge ${r.isActive ? 'green' : 'gray'}`}>
                        {r.isActive ? 'Active' : 'Inactive'}
                      </span>
                      {r.isArchived && <span className="badge amber">Archived</span>}
                    </td>
                    <td className="right actions">
                      <button className="btn btn-ghost xs" onClick={() => setDetailsId(r.agencyId)}>
                        View
                      </button>
                      <button
                        className="btn btn-ghost xs"
                        onClick={() => onTest(r)}
                        disabled={testingId === r.agencyId}
                      >
                        {testingId === r.agencyId ? '…' : 'Test'}
                      </button>
                      <button
                        className="btn btn-ghost xs"
                        onClick={() => setModal({ open: true, id: r.agencyId })}
                      >
                        Edit
                      </button>
                      <button className="btn btn-ghost xs" onClick={() => onArchive(r)}>
                        {r.isArchived ? 'Restore' : 'Archive'}
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </main>

      {detailsId !== null && (
        <AgencyDetailsModal
          agencyId={detailsId}
          onClose={() => setDetailsId(null)}
          onEdit={(id) => {
            setDetailsId(null)
            setModal({ open: true, id })
          }}
        />
      )}

      {deployOpen && <DeployModal onClose={() => setDeployOpen(false)} />}

      {modal.open && (
        <AgencyFormModal
          agencyId={modal.id}
          onClose={() => setModal({ open: false, id: null })}
          onSaved={() => {
            setModal({ open: false, id: null })
            showFlash('Saved.')
            load()
          }}
        />
      )}
    </div>
  )
}
