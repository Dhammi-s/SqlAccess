import { useEffect, useState } from 'react'
import {
  SECRET_TYPES,
  VaultApi,
  type SecretListItem,
  type SecretVersionItem,
} from '../api/vault'

export function SecretsPage() {
  const [secrets, setSecrets] = useState<SecretListItem[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [flash, setFlash] = useState<string | null>(null)

  const [createOpen, setCreateOpen] = useState(false)
  const [rotateFor, setRotateFor] = useState<SecretListItem | null>(null)
  const [versionsFor, setVersionsFor] = useState<SecretListItem | null>(null)

  function load(term = search) {
    setLoading(true)
    VaultApi.listSecrets(term || undefined)
      .then(setSecrets)
      .finally(() => setLoading(false))
  }
  useEffect(() => {
    load('')
  }, [])

  function showFlash(m: string) {
    setFlash(m)
    setTimeout(() => setFlash(null), 3000)
  }

  async function onDelete(s: SecretListItem) {
    if (!confirm(`Delete secret "${s.name}" and all its versions?`)) return
    await VaultApi.deleteSecret(s.secretId)
    showFlash('Secret deleted.')
    load()
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Secrets</h1>
          <p className="muted small">{secrets.length} shown</p>
        </div>
        <button className="btn btn-primary" onClick={() => setCreateOpen(true)}>
          + Create Secret
        </button>
      </div>

      <div className="toolbar">
        <input
          className="search"
          placeholder="Search name or type…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && load()}
        />
        <button className="btn btn-ghost sm" onClick={() => load()}>
          Search
        </button>
      </div>

      {flash && <div className="alert alert-ok" style={{ marginBottom: '1rem' }}>{flash}</div>}

      <div className="card table-card">
        <table className="tbl">
          <thead>
            <tr>
              <th>Secret</th>
              <th>Type</th>
              <th>Version</th>
              <th>Status</th>
              <th>Updated</th>
              <th className="right" style={{ textAlign: 'right' }}>
                Actions
              </th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={6} className="muted" style={{ textAlign: 'center' }}>
                  Loading…
                </td>
              </tr>
            ) : secrets.length === 0 ? (
              <tr>
                <td colSpan={6} className="muted" style={{ textAlign: 'center' }}>
                  No secrets.
                </td>
              </tr>
            ) : (
              secrets.map((s) => (
                <tr key={s.secretId}>
                  <td className="cell-title">{s.name}</td>
                  <td>
                    <span className="type-chip">{s.secretType}</span>
                  </td>
                  <td className="mono">v{s.currentVersion}</td>
                  <td>
                    <span className={`badge ${s.isActive ? 'Success' : 'Cancelled'}`}>
                      <span className="dot" />
                      {s.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="small">{s.updatedOn ? new Date(s.updatedOn).toLocaleString() : '—'}</td>
                  <td className="right">
                    <div className="row-actions">
                      <button className="btn btn-ghost xs" onClick={() => setRotateFor(s)}>
                        Rotate
                      </button>
                      <button className="btn btn-ghost xs" onClick={() => setVersionsFor(s)}>
                        Versions
                      </button>
                      <button className="btn btn-danger xs" onClick={() => onDelete(s)}>
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

      {createOpen && (
        <CreateSecretModal
          onClose={() => setCreateOpen(false)}
          onDone={() => {
            setCreateOpen(false)
            showFlash('Secret created.')
            load()
          }}
        />
      )}
      {rotateFor && (
        <RotateModal
          secret={rotateFor}
          onClose={() => setRotateFor(null)}
          onDone={() => {
            setRotateFor(null)
            showFlash('Secret rotated — new version created.')
            load()
          }}
        />
      )}
      {versionsFor && (
        <VersionsModal
          secret={versionsFor}
          onClose={() => setVersionsFor(null)}
          onRestored={() => {
            showFlash('Version restored as the new current version.')
            load()
          }}
        />
      )}
    </>
  )
}

function CreateSecretModal({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const [name, setName] = useState('')
  const [type, setType] = useState('Custom')
  const [value, setValue] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save() {
    setBusy(true)
    setError(null)
    try {
      await VaultApi.createSecret({ name: name.trim(), secretType: type, value })
      onDone()
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Failed to create secret.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="cicd-modal-backdrop" onMouseDown={onClose}>
      <div className="cicd-modal" onMouseDown={(e) => e.stopPropagation()}>
        <div className="cicd-modal-head">
          <h2>Create secret</h2>
          <button className="icon-btn" onClick={onClose}>
            ✕
          </button>
        </div>
        {error && <div className="alert alert-bad" style={{ marginBottom: '1rem' }}>{error}</div>}
        <div className="fields">
          <label>
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="OpenAI-Prod" />
          </label>
          <label>
            Type
            <select value={type} onChange={(e) => setType(e.target.value)}>
              {SECRET_TYPES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </label>
          <label>
            Value
            <textarea rows={3} value={value} onChange={(e) => setValue(e.target.value)} placeholder="the secret value…" />
          </label>
        </div>
        <div className="cicd-modal-actions">
          <button className="btn btn-ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={save} disabled={busy || !name.trim() || !value}>
            {busy ? 'Saving…' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  )
}

function RotateModal({ secret, onClose, onDone }: { secret: SecretListItem; onClose: () => void; onDone: () => void }) {
  const [value, setValue] = useState('')
  const [busy, setBusy] = useState(false)
  async function rotate() {
    setBusy(true)
    try {
      await VaultApi.rotateSecret(secret.secretId, value)
      onDone()
    } finally {
      setBusy(false)
    }
  }
  return (
    <div className="cicd-modal-backdrop" onMouseDown={onClose}>
      <div className="cicd-modal" onMouseDown={(e) => e.stopPropagation()}>
        <div className="cicd-modal-head">
          <h2>Rotate “{secret.name}”</h2>
          <button className="icon-btn" onClick={onClose}>
            ✕
          </button>
        </div>
        <p className="muted small">
          Current version v{secret.currentVersion}. Rotating stores a new encrypted version; applications immediately
          get the new value.
        </p>
        <div className="fields">
          <label>
            New value
            <textarea rows={3} value={value} onChange={(e) => setValue(e.target.value)} />
          </label>
        </div>
        <div className="cicd-modal-actions">
          <button className="btn btn-ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={rotate} disabled={busy || !value}>
            {busy ? 'Rotating…' : 'Rotate secret'}
          </button>
        </div>
      </div>
    </div>
  )
}

function VersionsModal({
  secret,
  onClose,
  onRestored,
}: {
  secret: SecretListItem
  onClose: () => void
  onRestored: () => void
}) {
  const [versions, setVersions] = useState<SecretVersionItem[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState<number | null>(null)

  function load() {
    setLoading(true)
    VaultApi.getVersions(secret.secretId)
      .then(setVersions)
      .finally(() => setLoading(false))
  }
  useEffect(load, [secret.secretId])

  async function restore(v: SecretVersionItem) {
    if (!confirm(`Restore v${v.version} as the new current version?`)) return
    setBusy(v.version)
    try {
      await VaultApi.restoreVersion(secret.secretId, v.version)
      onRestored()
      load()
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="cicd-modal-backdrop" onMouseDown={onClose}>
      <div className="cicd-modal wide" onMouseDown={(e) => e.stopPropagation()}>
        <div className="cicd-modal-head">
          <h2>“{secret.name}” — versions</h2>
          <button className="icon-btn" onClick={onClose}>
            ✕
          </button>
        </div>
        <div className="card table-card">
          <table className="tbl">
            <thead>
              <tr>
                <th>Version</th>
                <th>Current</th>
                <th>Created</th>
                <th>By</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={5} className="muted" style={{ textAlign: 'center' }}>
                    Loading…
                  </td>
                </tr>
              ) : (
                versions.map((v) => (
                  <tr key={v.secretVersionId}>
                    <td className="mono">v{v.version}</td>
                    <td>{v.isCurrent ? <span className="badge Success"><span className="dot" />current</span> : '—'}</td>
                    <td className="small">{new Date(v.createdOn).toLocaleString()}</td>
                    <td className="small">{v.createdBy ?? '—'}</td>
                    <td>
                      {!v.isCurrent && (
                        <button className="btn btn-ghost xs" onClick={() => restore(v)} disabled={busy === v.version}>
                          {busy === v.version ? '…' : 'Restore'}
                        </button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <p className="muted small" style={{ marginTop: '0.6rem' }}>
          Values are never displayed — restore copies the chosen version forward as a new encrypted current version.
        </p>
      </div>
    </div>
  )
}
