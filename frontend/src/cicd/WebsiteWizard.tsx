import { useEffect, useState } from 'react'
import { WebsitesApi, type BuildTemplate, type UpsertWebsite } from '../api/cicd'

interface Props {
  websiteId: number | null
  onClose: () => void
  onSaved: () => void
}

const PROJECT_TYPES = ['AspNetCore', 'ReactVite', 'Angular', 'Node', 'Static']

const empty: UpsertWebsite = {
  websiteName: '',
  repositoryUrl: '',
  gitProvider: 'GitHub',
  defaultBranch: '',
  projectType: 'AspNetCore',
  gitPat: '',
  buildCommand: '',
  publishCommand: '',
  publishFolder: '',
  deployProvider: 'SFTP',
  workflowFile: 'deploy.yml',
  ftpHost: '',
  ftpPort: 22,
  ftpUsername: '',
  ftpPassword: '',
  ftpRootFolder: '/',
  isActive: true,
}

const STEP_TITLES = ['General', 'Git', 'Branch', 'Build', 'FTP']

export function WebsiteWizard({ websiteId, onClose, onSaved }: Props) {
  const isEdit = websiteId !== null
  const [step, setStep] = useState(1)
  const [form, setForm] = useState<UpsertWebsite>(empty)
  const [mode, setMode] = useState<'actions' | 'local'>('actions')
  const [templates, setTemplates] = useState<BuildTemplate[]>([])
  const [loading, setLoading] = useState(isEdit)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [gitTest, setGitTest] = useState<{ ok: boolean; msg: string } | null>(null)
  const [testingGit, setTestingGit] = useState(false)
  const [branches, setBranches] = useState<string[]>([])
  const [loadingBranches, setLoadingBranches] = useState(false)
  const [ftpTest, setFtpTest] = useState<{ ok: boolean; msg: string } | null>(null)
  const [testingFtp, setTestingFtp] = useState(false)

  useEffect(() => {
    WebsitesApi.buildTemplates().then(setTemplates)
  }, [])

  useEffect(() => {
    if (!isEdit) return
    setLoading(true)
    WebsitesApi.get(websiteId!)
      .then((d) => {
        setMode(d.workflowFile ? 'actions' : 'local')
        setForm({
          websiteName: d.websiteName,
          repositoryUrl: d.repositoryUrl ?? '',
          gitProvider: d.gitProvider,
          defaultBranch: d.defaultBranch ?? '',
          projectType: d.projectType,
          gitPat: '',
          buildCommand: d.buildCommand ?? '',
          publishCommand: d.publishCommand ?? '',
          publishFolder: d.publishFolder ?? '',
          deployProvider: d.deployProvider ?? 'FTP',
          workflowFile: d.workflowFile ?? '',
          ftpHost: d.ftpHost ?? '',
          ftpPort: d.ftpPort,
          ftpUsername: d.ftpUsername ?? '',
          ftpPassword: '',
          ftpRootFolder: d.ftpRootFolder ?? '/',
          isActive: d.isActive,
        })
      })
      .finally(() => setLoading(false))
  }, [websiteId, isEdit])

  function set<K extends keyof UpsertWebsite>(key: K, value: UpsertWebsite[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function applyTemplate(projectType: string) {
    const t = templates.find((x) => x.projectType === projectType)
    setForm((f) => ({
      ...f,
      projectType,
      buildCommand: t?.buildCommand ?? f.buildCommand,
      publishCommand: t?.publishCommand ?? f.publishCommand,
      publishFolder: t?.publishFolder ?? f.publishFolder,
    }))
  }

  async function testGit() {
    setTestingGit(true)
    setGitTest(null)
    try {
      const r = await WebsitesApi.testGit(form.repositoryUrl ?? '', form.gitPat || null)
      setGitTest({ ok: r.success, msg: r.message })
    } catch (e: any) {
      setGitTest({ ok: false, msg: e.message ?? 'Failed.' })
    } finally {
      setTestingGit(false)
    }
  }

  async function loadBranches() {
    setLoadingBranches(true)
    try {
      const list = isEdit
        ? await WebsitesApi.branches(websiteId!)
        : await WebsitesApi.previewBranches(form.repositoryUrl ?? '', form.gitPat || null)
      const names = list.map((b) => b.name)
      setBranches(names)
      if (!form.defaultBranch && names.length)
        set('defaultBranch', names.includes('main') ? 'main' : names.includes('master') ? 'master' : names[0])
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Could not load branches.')
    } finally {
      setLoadingBranches(false)
    }
  }

  async function testFtp() {
    setTestingFtp(true)
    setFtpTest(null)
    try {
      const r = await WebsitesApi.testFtp({
        host: form.ftpHost ?? '',
        port: form.ftpPort,
        username: form.ftpUsername ?? '',
        password: form.ftpPassword || null,
        rootFolder: form.ftpRootFolder,
        provider: form.deployProvider,
      })
      setFtpTest({ ok: r.success, msg: r.message })
    } catch (e: any) {
      setFtpTest({ ok: false, msg: e.message ?? 'Failed.' })
    } finally {
      setTestingFtp(false)
    }
  }

  function next() {
    setError(null)
    if (step === 2 && !isEdit && branches.length === 0) loadBranches()
    setStep((s) => Math.min(5, s + 1))
  }

  async function save() {
    setSaving(true)
    setError(null)
    try {
      const payload: UpsertWebsite = {
        ...form,
        workflowFile: mode === 'actions' ? form.workflowFile || 'deploy.yml' : '',
      }
      if (isEdit) await WebsitesApi.update(websiteId!, payload)
      else await WebsitesApi.create(payload)
      onSaved()
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="cicd-modal-backdrop" onMouseDown={onClose}>
      <div className="cicd-modal wide" onMouseDown={(e) => e.stopPropagation()}>
        <div className="cicd-modal-head">
          <h2>{isEdit ? 'Edit website' : 'Create website'}</h2>
          <button className="icon-btn" onClick={onClose}>
            ✕
          </button>
        </div>

        <div className="steps">
          {STEP_TITLES.map((t, i) => (
            <div key={t} className={`step ${step === i + 1 ? 'active' : step > i + 1 ? 'done' : ''}`}>
              <span className="num">{step > i + 1 ? '✓' : i + 1}</span> {t}
            </div>
          ))}
        </div>

        {error && <div className="alert alert-bad" style={{ marginBottom: '1rem' }}>{error}</div>}
        {loading ? (
          <p className="muted">Loading…</p>
        ) : (
          <div className="fields">
            {step === 1 && (
              <>
                <label>
                  Website name
                  <input value={form.websiteName} onChange={(e) => set('websiteName', e.target.value)} />
                </label>
                <label>
                  Repository URL
                  <input
                    placeholder="https://github.com/owner/repo"
                    value={form.repositoryUrl ?? ''}
                    onChange={(e) => set('repositoryUrl', e.target.value)}
                  />
                </label>
                <label>
                  Project type
                  <select value={form.projectType} onChange={(e) => applyTemplate(e.target.value)}>
                    {PROJECT_TYPES.map((p) => (
                      <option key={p} value={p}>
                        {p}
                      </option>
                    ))}
                  </select>
                </label>
              </>
            )}

            {step === 2 && (
              <>
                <label>
                  Repository URL
                  <input value={form.repositoryUrl ?? ''} onChange={(e) => set('repositoryUrl', e.target.value)} />
                </label>
                <label>
                  Personal Access Token {isEdit && <span className="muted">(blank = keep current)</span>}
                  <input
                    type="password"
                    placeholder="ghp_… (only for private repos)"
                    value={form.gitPat ?? ''}
                    onChange={(e) => set('gitPat', e.target.value)}
                  />
                </label>
                <div>
                  <button className="btn btn-ghost sm" onClick={testGit} disabled={testingGit}>
                    {testingGit ? 'Testing…' : 'Test Connection'}
                  </button>
                  {gitTest && (
                    <div className={`alert ${gitTest.ok ? 'alert-ok' : 'alert-bad'}`} style={{ marginTop: '0.6rem' }}>
                      {gitTest.ok ? 'Connected Successfully — ' : 'Connection Failed — '}
                      {gitTest.msg}
                    </div>
                  )}
                </div>
              </>
            )}

            {step === 3 && (
              <>
                <div className="switch-row" style={{ justifyContent: 'space-between' }}>
                  <span className="muted small">Branches loaded from {form.gitProvider}</span>
                  <button className="btn btn-ghost xs" onClick={loadBranches} disabled={loadingBranches}>
                    {loadingBranches ? 'Loading…' : 'Refresh'}
                  </button>
                </div>
                <label>
                  Default branch
                  <select value={form.defaultBranch ?? ''} onChange={(e) => set('defaultBranch', e.target.value)}>
                    {branches.length === 0 && <option value="">{loadingBranches ? 'Loading…' : '(none loaded)'}</option>}
                    {branches.map((b) => (
                      <option key={b} value={b}>
                        {b}
                      </option>
                    ))}
                  </select>
                </label>
              </>
            )}

            {step === 4 && (
              <>
                <div className="mode-row">
                  <label className="switch-row">
                    <input type="radio" name="buildmode" checked={mode === 'actions'} onChange={() => setMode('actions')} />
                    Build with GitHub Actions (works on shared hosting)
                  </label>
                  <label className="switch-row">
                    <input type="radio" name="buildmode" checked={mode === 'local'} onChange={() => setMode('local')} />
                    Build on this machine (self-hosted)
                  </label>
                </div>

                {mode === 'actions' ? (
                  <>
                    <label>
                      Workflow file (in .github/workflows/)
                      <input
                        placeholder="deploy.yml"
                        value={form.workflowFile ?? ''}
                        onChange={(e) => set('workflowFile', e.target.value)}
                      />
                    </label>
                    <div className="alert alert-info">
                      The portal triggers this workflow on GitHub, streams its steps here, and records history.
                      GitHub's runner does the build &amp; upload — your repo needs a{' '}
                      <code>workflow_dispatch</code> workflow. (FTP settings below are not used in this mode.)
                    </div>
                  </>
                ) : (
                  <>
                    <p className="muted small">Suggested for {form.projectType} — edit as needed.</p>
                    <label>
                      Build command
                      <textarea rows={2} value={form.buildCommand ?? ''} onChange={(e) => set('buildCommand', e.target.value)} />
                    </label>
                    <label>
                      Publish command
                      <textarea rows={2} value={form.publishCommand ?? ''} onChange={(e) => set('publishCommand', e.target.value)} />
                    </label>
                    <label>
                      Publish folder
                      <input value={form.publishFolder ?? ''} onChange={(e) => set('publishFolder', e.target.value)} />
                    </label>
                  </>
                )}
              </>
            )}

            {step === 5 && (
              <>
                <div className="grid2">
                  <label>
                    Protocol
                    <select
                      value={form.deployProvider}
                      onChange={(e) => {
                        const p = e.target.value
                        setForm((f) => ({ ...f, deployProvider: p, ftpPort: p === 'SFTP' ? 22 : 21 }))
                      }}
                    >
                      <option value="SFTP">SFTP (port 22 — recommended)</option>
                      <option value="FTP">FTP (port 21)</option>
                    </select>
                  </label>
                  <label>
                    Port
                    <input
                      type="number"
                      value={form.ftpPort}
                      onChange={(e) => set('ftpPort', parseInt(e.target.value) || 21)}
                    />
                  </label>
                  <label>
                    Host
                    <input value={form.ftpHost ?? ''} onChange={(e) => set('ftpHost', e.target.value)} />
                  </label>
                  <label>
                    Root folder
                    <input value={form.ftpRootFolder ?? ''} onChange={(e) => set('ftpRootFolder', e.target.value)} />
                  </label>
                  <label>
                    Username
                    <input value={form.ftpUsername ?? ''} onChange={(e) => set('ftpUsername', e.target.value)} />
                  </label>
                  <label>
                    Password {isEdit && <span className="muted">(blank = keep)</span>}
                    <input
                      type="password"
                      value={form.ftpPassword ?? ''}
                      onChange={(e) => set('ftpPassword', e.target.value)}
                    />
                  </label>
                </div>
                <div>
                  <button className="btn btn-ghost sm" onClick={testFtp} disabled={testingFtp}>
                    {testingFtp ? 'Testing…' : `Test ${form.deployProvider}`}
                  </button>
                  {ftpTest && (
                    <div className={`alert ${ftpTest.ok ? 'alert-ok' : 'alert-bad'}`} style={{ marginTop: '0.6rem' }}>
                      {ftpTest.ok ? 'Connection OK — ' : 'Failed — '}
                      {ftpTest.msg}
                    </div>
                  )}
                </div>
              </>
            )}
          </div>
        )}

        <div className="cicd-modal-actions">
          <button className="btn btn-ghost" onClick={() => (step === 1 ? onClose() : setStep((s) => s - 1))}>
            {step === 1 ? 'Cancel' : 'Back'}
          </button>
          {step < 5 ? (
            <button className="btn btn-primary" onClick={next} disabled={step === 1 && !form.websiteName}>
              Next
            </button>
          ) : (
            <button className="btn btn-primary" onClick={save} disabled={saving}>
              {saving ? 'Saving…' : 'Save website'}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
