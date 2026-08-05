import { useCallback, useEffect, useState, type FormEvent } from 'react'
import {
  AgenciesApi,
  type AgencyDetail,
  type DbRole,
  type DbRolesResult,
} from '../api/agencies'

interface Props {
  agencyId: number
  onClose: () => void
  onEdit: (id: number) => void
}

export function AgencyDetailsModal({ agencyId, onClose, onEdit }: Props) {
  const [detail, setDetail] = useState<AgencyDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [showSecrets, setShowSecrets] = useState(false)

  const [roles, setRoles] = useState<DbRolesResult | null>(null)
  const [rolesLoading, setRolesLoading] = useState(false)

  const [newRole, setNewRole] = useState('')
  const [readOnly, setReadOnly] = useState(true)
  const [creating, setCreating] = useState(false)
  const [roleMsg, setRoleMsg] = useState<{ ok: boolean; text: string } | null>(null)

  useEffect(() => {
    setLoading(true)
    AgenciesApi.get(agencyId)
      .then(setDetail)
      .finally(() => setLoading(false))
  }, [agencyId])

  const loadRoles = useCallback(() => {
    setRolesLoading(true)
    setRoles(null)
    AgenciesApi.roles(agencyId)
      .then(setRoles)
      .catch((e) => setRoles({ success: false, message: e.message ?? 'Failed to load roles.', totalRoles: 0, roles: [] }))
      .finally(() => setRolesLoading(false))
  }, [agencyId])

  useEffect(() => {
    loadRoles()
  }, [loadRoles])

  async function onCreateRole(e: FormEvent) {
    e.preventDefault()
    setRoleMsg(null)
    setCreating(true)
    try {
      const res = await AgenciesApi.createRole(agencyId, newRole.trim(), readOnly)
      setRoleMsg({ ok: res.success, text: res.message })
      if (res.success) {
        setNewRole('')
        loadRoles()
      }
    } catch (err: any) {
      setRoleMsg({ ok: false, text: err.response?.data?.message ?? err.message ?? 'Create failed.' })
    } finally {
      setCreating(false)
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal card modal-lg" onMouseDown={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h2>{detail?.agencyName ?? 'Agency details'}</h2>
          <button className="icon-btn" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>

        {loading || !detail ? (
          <p className="muted">Loading…</p>
        ) : (
          <>
            {/* ---------- Details ---------- */}
            <section className="detail-grid">
              <Field label="Agency name" value={detail.agencyName} />
              <Field label="Location" value={detail.location} />
              <Field label="Domain URL" value={detail.domainUrl} />
              <Field
                label="Status"
                value={`${detail.isActive ? 'Active' : 'Inactive'}${detail.isArchived ? ' • Archived' : ''}`}
              />
              <Field label="DB Server" value={detail.dbServer} mono />
              <Field label="DB Name" value={detail.dbName} mono />
              <Field label="DB User" value={detail.dbUser} mono />
              <Field
                label="DB Password"
                mono
                value={showSecrets ? detail.dbPassword || '—' : mask(detail.dbPassword)}
              />
              <div className="detail-item span2">
                <span className="detail-label">Connection string</span>
                <span className="detail-value mono wrap">
                  {showSecrets ? detail.connectionString || '—' : mask(detail.connectionString)}
                </span>
              </div>
              <Field label="Created" value={fmt(detail.createdOn)} />
              <Field label="Updated" value={detail.updatedOn ? fmt(detail.updatedOn) : '—'} />
            </section>

            <div className="detail-actions">
              <button className="btn btn-ghost sm" onClick={() => setShowSecrets((s) => !s)}>
                {showSecrets ? 'Hide secrets' : 'Reveal secrets'}
              </button>
              <button className="btn btn-ghost sm" onClick={() => onEdit(detail.agencyId)}>
                Edit agency
              </button>
            </div>

            {/* ---------- Database roles / security ---------- */}
            <fieldset className="roles-section">
              <legend>
                Database security — roles
                {roles?.success ? <span className="badge green">{roles.totalRoles} total</span> : null}
              </legend>

              {rolesLoading ? (
                <p className="muted">Connecting to the database…</p>
              ) : !roles?.success ? (
                <div className="alert alert-error">
                  Could not read roles: {roles?.message}
                </div>
              ) : (
                <div className="table-card roles-table">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Role</th>
                        <th>Type</th>
                        <th className="right">Members</th>
                        <th>Member list</th>
                      </tr>
                    </thead>
                    <tbody>
                      {roles.roles.map((r: DbRole) => (
                        <tr key={r.roleName}>
                          <td className="mono cell-title">{r.roleName}</td>
                          <td>
                            <span className={`badge ${r.isFixedRole ? 'gray' : 'green'}`}>
                              {r.isFixedRole ? 'Fixed' : 'Custom'}
                            </span>
                          </td>
                          <td className="right">{r.memberCount}</td>
                          <td className="muted small">{r.members || '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {/* Create role */}
              <form className="create-role" onSubmit={onCreateRole}>
                <div className="create-role-row">
                  <input
                    placeholder="New role name (e.g. ReportViewer)"
                    value={newRole}
                    onChange={(e) => setNewRole(e.target.value)}
                    pattern="[A-Za-z_][A-Za-z0-9_]*"
                    title="Start with a letter/underscore; letters, numbers, underscores only"
                    required
                  />
                  <label className="switch-row">
                    <input type="checkbox" checked={readOnly} onChange={(e) => setReadOnly(e.target.checked)} />
                    Read-only (GRANT SELECT)
                  </label>
                  <button className="btn btn-primary" type="submit" disabled={creating || !roles?.success}>
                    {creating ? 'Creating…' : 'Add role'}
                  </button>
                </div>
                {roleMsg && (
                  <div className={`alert ${roleMsg.ok ? 'alert-info' : 'alert-error'}`}>{roleMsg.text}</div>
                )}
              </form>
            </fieldset>
          </>
        )}
      </div>
    </div>
  )
}

function Field({ label, value, mono }: { label: string; value?: string | null; mono?: boolean }) {
  return (
    <div className="detail-item">
      <span className="detail-label">{label}</span>
      <span className={`detail-value ${mono ? 'mono' : ''}`}>{value || '—'}</span>
    </div>
  )
}

function mask(secret?: string | null) {
  if (!secret) return '—'
  return secret.length <= 2 ? '••••' : `${secret[0]}••••${secret[secret.length - 1]}`
}

function fmt(iso: string) {
  const d = new Date(iso)
  return isNaN(d.getTime()) ? iso : d.toLocaleString()
}
