import { useEffect, useState, type ChangeEvent } from 'react'
import { AgenciesApi, type AgencyListItem } from '../api/agencies'
import { DeployApi, type DacpacInfo, type DeployResult } from '../api/deploy'

interface Props {
  onClose: () => void
}

type Source = 'github' | 'upload'
type RowStatus = 'idle' | 'running' | 'done' | 'error'
interface Row {
  status: RowStatus
  result?: DeployResult
}

export function DeployModal({ onClose }: Props) {
  const [agencies, setAgencies] = useState<AgencyListItem[]>([])
  const [selected, setSelected] = useState<Set<number>>(new Set())

  const [source, setSource] = useState<Source>('github')
  const [branches, setBranches] = useState<string[]>([])
  const [branch, setBranch] = useState('')
  const [branchErr, setBranchErr] = useState<string | null>(null)

  const [dacpac, setDacpac] = useState<DacpacInfo | null>(null)
  const [uploading, setUploading] = useState(false)
  const [uploadErr, setUploadErr] = useState<string | null>(null)

  const [generateScriptOnly, setGenerateScriptOnly] = useState(true)
  const [blockOnDataLoss, setBlockOnDataLoss] = useState(false)
  const [dropObjects, setDropObjects] = useState(false)

  const [running, setRunning] = useState(false)
  const [buildMsg, setBuildMsg] = useState<string | null>(null)
  const [rows, setRows] = useState<Record<number, Row>>({})

  useEffect(() => {
    AgenciesApi.list(false).then((list) => {
      const active = list.filter((a) => a.isActive && !a.isArchived)
      setAgencies(active)
      setSelected(new Set(active.map((a) => a.agencyId)))
    })
  }, [])

  useEffect(() => {
    if (source !== 'github') return
    setBranchErr(null)
    DeployApi.branches()
      .then((bs) => {
        const names = bs.map((b) => b.name)
        setBranches(names)
        setBranch((cur) => cur || (names.includes('master') ? 'master' : names[0] ?? ''))
      })
      .catch((e) => setBranchErr(e.response?.data?.message ?? 'Could not load branches.'))
  }, [source])

  async function onFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setUploadErr(null)
    setUploading(true)
    setDacpac(null)
    try {
      setDacpac(await DeployApi.upload(file))
    } catch (err: any) {
      setUploadErr(err.response?.data?.message ?? err.message ?? 'Upload failed.')
    } finally {
      setUploading(false)
    }
  }

  function toggle(id: number) {
    setSelected((s) => {
      const n = new Set(s)
      n.has(id) ? n.delete(id) : n.add(id)
      return n
    })
  }

  function toggleAll() {
    setSelected((s) => (s.size === agencies.length ? new Set() : new Set(agencies.map((a) => a.agencyId))))
  }

  async function run() {
    if (selected.size === 0) return
    if (source === 'upload' && !dacpac) return
    if (!generateScriptOnly) {
      const ok = confirm(
        `DEPLOY will apply schema changes to ${selected.size} live database(s). This modifies real tenant data. Continue?`,
      )
      if (!ok) return
    }
    setRunning(true)
    setBuildMsg(null)
    setRows({})

    // Step 0 — build the DACPAC from GitHub if that's the source.
    let dacpacId = dacpac?.dacpacId
    if (source === 'github') {
      setBuildMsg(`Building DACPAC from branch '${branch}'…`)
      try {
        const res = await DeployApi.build(branch)
        if (!res.success || !res.dacpac) {
          setBuildMsg('✕ ' + res.message)
          setRunning(false)
          return
        }
        setDacpac(res.dacpac)
        dacpacId = res.dacpac.dacpacId
        setBuildMsg('✓ ' + res.message)
      } catch (err: any) {
        setBuildMsg('✕ ' + (err.response?.data?.message ?? err.message ?? 'Build failed.'))
        setRunning(false)
        return
      }
    }
    if (!dacpacId) {
      setRunning(false)
      return
    }

    const targets = agencies.filter((a) => selected.has(a.agencyId))
    setRows(Object.fromEntries(targets.map((a) => [a.agencyId, { status: 'idle' } as Row])))

    for (const a of targets) {
      setRows((r) => ({ ...r, [a.agencyId]: { status: 'running' } }))
      try {
        const result = await DeployApi.run({
          dacpacId,
          agencyId: a.agencyId,
          generateScriptOnly,
          blockOnPossibleDataLoss: blockOnDataLoss,
          dropObjectsNotInSource: dropObjects,
        })
        setRows((r) => ({ ...r, [a.agencyId]: { status: result.success ? 'done' : 'error', result } }))
      } catch (err: any) {
        setRows((r) => ({
          ...r,
          [a.agencyId]: {
            status: 'error',
            result: {
              agencyId: a.agencyId,
              agencyName: a.agencyName,
              targetServer: a.dbServer ?? '',
              targetDatabase: a.dbName ?? '',
              success: false,
              message: err.response?.data?.message ?? err.message ?? 'Request failed.',
              elapsedMs: 0,
              scriptGenerated: false,
              script: null,
            },
          },
        }))
      }
    }
    setRunning(false)
  }

  function downloadScript(res: DeployResult) {
    const blob = new Blob([res.script ?? ''], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${res.targetDatabase || 'deploy'}.sql`
    a.click()
    URL.revokeObjectURL(url)
  }

  const allSelected = selected.size === agencies.length && agencies.length > 0

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal card modal-lg" onMouseDown={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h2>Deploy database schema (DACPAC)</h2>
          <button className="icon-btn" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>

        {/* 1. Source */}
        <fieldset>
          <legend>1 · Source</legend>
          <div className="mode-row">
            <label className="switch-row">
              <input
                type="radio"
                name="source"
                checked={source === 'github'}
                onChange={() => setSource('github')}
              />
              Build from GitHub
            </label>
            <label className="switch-row">
              <input
                type="radio"
                name="source"
                checked={source === 'upload'}
                onChange={() => setSource('upload')}
              />
              Upload a .dacpac
            </label>
          </div>

          {source === 'github' ? (
            <>
              <label>
                Branch
                <select
                  value={branch}
                  onChange={(e) => {
                    setBranch(e.target.value)
                    setDacpac(null) // force rebuild for the new branch
                  }}
                  disabled={running || branches.length === 0}
                >
                  {branches.length === 0 && <option>Loading…</option>}
                  {branches.map((b) => (
                    <option key={b} value={b}>
                      {b}
                    </option>
                  ))}
                </select>
              </label>
              <p className="muted small">
                Dhammi-s/DB-WorkProvider360 — the DACPAC is built on the server when you run.
              </p>
              {branchErr && <div className="alert alert-error">{branchErr}</div>}
            </>
          ) : (
            <>
              <input type="file" accept=".dacpac" onChange={onFile} disabled={uploading || running} />
              {uploading && <p className="muted">Uploading & validating…</p>}
              {uploadErr && <div className="alert alert-error">{uploadErr}</div>}
              {dacpac && (
                <p className="muted small">
                  ✓ {dacpac.fileName} ({(dacpac.sizeBytes / 1024).toFixed(1)} KB) ready
                </p>
              )}
            </>
          )}
          {buildMsg && (
            <div className={`alert ${buildMsg.startsWith('✕') ? 'alert-error' : 'alert-info'}`}>{buildMsg}</div>
          )}
        </fieldset>

        {/* 2. Mode */}
        <fieldset>
          <legend>2 · Action</legend>
          <div className="mode-row">
            <label className="switch-row">
              <input
                type="radio"
                name="mode"
                checked={generateScriptOnly}
                onChange={() => setGenerateScriptOnly(true)}
              />
              Generate script (preview, no changes)
            </label>
            <label className="switch-row">
              <input
                type="radio"
                name="mode"
                checked={!generateScriptOnly}
                onChange={() => setGenerateScriptOnly(false)}
              />
              Deploy (publish changes)
            </label>
          </div>
          <div className="mode-row">
            <label className="switch-row">
              <input type="checkbox" checked={blockOnDataLoss} onChange={(e) => setBlockOnDataLoss(e.target.checked)} />
              Block on possible data loss
            </label>
            <label className="switch-row">
              <input type="checkbox" checked={dropObjects} onChange={(e) => setDropObjects(e.target.checked)} />
              Drop objects not in source
            </label>
          </div>
          {!generateScriptOnly && (
            <div className="alert alert-error">
              ⚠ Deploy applies schema changes directly to the selected live databases.
            </div>
          )}
        </fieldset>

        {/* 3. Targets */}
        <fieldset>
          <legend>3 · Target agencies ({selected.size} selected)</legend>
          <label className="switch-row" style={{ marginBottom: '0.5rem' }}>
            <input type="checkbox" checked={allSelected} onChange={toggleAll} />
            Select all active
          </label>
          <div className="target-list">
            {agencies.map((a) => {
              const row = rows[a.agencyId]
              return (
                <div key={a.agencyId} className="target-item">
                  <label className="switch-row">
                    <input
                      type="checkbox"
                      checked={selected.has(a.agencyId)}
                      onChange={() => toggle(a.agencyId)}
                      disabled={running}
                    />
                    <span className="cell-title">{a.agencyName}</span>
                    <span className="muted small mono">
                      {a.dbServer}/{a.dbName}
                    </span>
                  </label>
                  {row && (
                    <span className={`deploy-status ${row.status}`}>
                      {row.status === 'running' && '⏳ running…'}
                      {row.status === 'done' && `✓ ${row.result?.message} (${row.result?.elapsedMs} ms)`}
                      {row.status === 'error' && `✕ ${row.result?.message}`}
                      {row.status === 'done' && row.result?.scriptGenerated && (
                        <button className="btn btn-ghost xs" onClick={() => downloadScript(row.result!)}>
                          Download .sql
                        </button>
                      )}
                    </span>
                  )}
                </div>
              )
            })}
          </div>
        </fieldset>

        <div className="modal-actions">
          <button className="btn btn-ghost" onClick={onClose} disabled={running}>
            Close
          </button>
          <button
            className="btn btn-primary"
            onClick={run}
            disabled={
              selected.size === 0 ||
              running ||
              (source === 'upload' && !dacpac) ||
              (source === 'github' && !branch)
            }
          >
            {running ? 'Running…' : generateScriptOnly ? 'Build & generate scripts' : 'Build & deploy now'}
          </button>
        </div>
      </div>
    </div>
  )
}
