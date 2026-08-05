import { useEffect, useState, type FormEvent } from 'react'
import { AgenciesApi, type AgencyUpsert, type TestConnectionResult } from '../api/agencies'

interface Props {
  agencyId: number | null // null = create
  onClose: () => void
  onSaved: () => void
}

const empty: AgencyUpsert = {
  agencyName: '',
  domainUrl: '',
  location: '',
  dbServer: '',
  dbName: '',
  dbUser: '',
  dbPassword: '',
  connectionString: '',
  isActive: true,
}

export function AgencyFormModal({ agencyId, onClose, onSaved }: Props) {
  const isEdit = agencyId !== null
  const [form, setForm] = useState<AgencyUpsert>(empty)
  const [loading, setLoading] = useState(isEdit)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<TestConnectionResult | null>(null)
  const [testing, setTesting] = useState(false)

  useEffect(() => {
    if (!isEdit) return
    setLoading(true)
    AgenciesApi.get(agencyId!)
      .then((d) =>
        setForm({
          agencyName: d.agencyName,
          domainUrl: d.domainUrl ?? '',
          location: d.location ?? '',
          dbServer: d.dbServer ?? '',
          dbName: d.dbName ?? '',
          dbUser: d.dbUser ?? '',
          dbPassword: '', // blank = keep existing on save
          connectionString: d.connectionString ?? '',
          isActive: d.isActive,
        }),
      )
      .catch(() => setError('Failed to load agency.'))
      .finally(() => setLoading(false))
  }, [agencyId, isEdit])

  function set<K extends keyof AgencyUpsert>(key: K, value: AgencyUpsert[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function onTest() {
    setTesting(true)
    setTestResult(null)
    try {
      if (form.connectionString?.trim()) {
        setTestResult(await AgenciesApi.testAdHoc(form.connectionString))
      } else if (isEdit) {
        setTestResult(await AgenciesApi.test(agencyId!))
      } else {
        setTestResult({
          success: false,
          message: 'Enter a connection string to test, or save first.',
          elapsedMs: 0,
        })
      }
    } catch (err: any) {
      setTestResult({ success: false, message: err.message ?? 'Test failed.', elapsedMs: 0 })
    } finally {
      setTesting(false)
    }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setSaving(true)
    try {
      if (isEdit) await AgenciesApi.update(agencyId!, form)
      else await AgenciesApi.create(form)
      onSaved()
    } catch (err: any) {
      setError(err.response?.data?.message ?? 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal card" onMouseDown={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h2>{isEdit ? 'Edit agency' : 'Add agency'}</h2>
          <button className="icon-btn" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>

        {loading ? (
          <p className="muted">Loading…</p>
        ) : (
          <form onSubmit={onSubmit}>
            {error && <div className="alert alert-error">{error}</div>}

            <div className="grid2">
              <label>
                Agency name *
                <input
                  value={form.agencyName}
                  onChange={(e) => set('agencyName', e.target.value)}
                  required
                />
              </label>
              <label>
                Location
                <input value={form.location ?? ''} onChange={(e) => set('location', e.target.value)} />
              </label>
              <label>
                Domain URL
                <input value={form.domainUrl ?? ''} onChange={(e) => set('domainUrl', e.target.value)} />
              </label>
              <label className="switch-row">
                <input
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(e) => set('isActive', e.target.checked)}
                />
                Active
              </label>
            </div>

            <fieldset>
              <legend>Database connection</legend>
              <div className="grid2">
                <label>
                  DB server
                  <input value={form.dbServer ?? ''} onChange={(e) => set('dbServer', e.target.value)} />
                </label>
                <label>
                  DB name
                  <input value={form.dbName ?? ''} onChange={(e) => set('dbName', e.target.value)} />
                </label>
                <label>
                  DB user
                  <input value={form.dbUser ?? ''} onChange={(e) => set('dbUser', e.target.value)} />
                </label>
                <label>
                  DB password {isEdit && <span className="muted">(blank = keep current)</span>}
                  <input
                    type="password"
                    value={form.dbPassword ?? ''}
                    onChange={(e) => set('dbPassword', e.target.value)}
                    autoComplete="new-password"
                  />
                </label>
              </div>

              <label>
                Connection string <span className="muted">(optional — auto-built from fields above if empty)</span>
                <textarea
                  rows={3}
                  value={form.connectionString ?? ''}
                  onChange={(e) => set('connectionString', e.target.value)}
                  placeholder="Server=…;Database=…;User Id=…;Password=…;Encrypt=True;"
                />
              </label>

              <div className="test-row">
                <button type="button" className="btn btn-ghost" onClick={onTest} disabled={testing}>
                  {testing ? 'Testing…' : 'Test connection'}
                </button>
                {testResult && (
                  <span className={`test-result ${testResult.success ? 'ok' : 'bad'}`}>
                    {testResult.success ? '✓' : '✕'} {testResult.message}
                    {testResult.elapsedMs ? ` (${testResult.elapsedMs} ms)` : ''}
                  </span>
                )}
              </div>
            </fieldset>

            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={onClose}>
                Cancel
              </button>
              <button type="submit" className="btn btn-primary" disabled={saving}>
                {saving ? 'Saving…' : isEdit ? 'Save changes' : 'Create agency'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
