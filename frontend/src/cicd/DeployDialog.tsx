import { useEffect, useState } from 'react'
import { DeploymentsApi, WebsitesApi, type CommitInfo, type WebsiteListItem } from '../api/cicd'

export function DeployDialog({
  website,
  onClose,
  onStarted,
}: {
  website: WebsiteListItem
  onClose: () => void
  onStarted: (deploymentId: number) => void
}) {
  const [branches, setBranches] = useState<string[]>([])
  const [branch, setBranch] = useState(website.defaultBranch ?? '')
  const [commit, setCommit] = useState<CommitInfo | null>(null)
  const [loadingBranches, setLoadingBranches] = useState(true)
  const [loadingCommit, setLoadingCommit] = useState(false)
  const [starting, setStarting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    WebsitesApi.branches(website.websiteId)
      .then((list) => {
        const names = list.map((b) => b.name)
        setBranches(names)
        const initial = website.defaultBranch && names.includes(website.defaultBranch) ? website.defaultBranch : names[0] ?? ''
        setBranch(initial)
      })
      .catch((e) => setError(e.response?.data?.message ?? 'Could not load branches.'))
      .finally(() => setLoadingBranches(false))
  }, [website])

  useEffect(() => {
    if (!branch) return
    setLoadingCommit(true)
    setCommit(null)
    WebsitesApi.latestCommit(website.websiteId, branch)
      .then(setCommit)
      .catch(() => setCommit(null))
      .finally(() => setLoadingCommit(false))
  }, [branch, website.websiteId])

  async function start() {
    setStarting(true)
    setError(null)
    try {
      const { deploymentId } = await DeploymentsApi.trigger(website.websiteId, branch)
      onStarted(deploymentId)
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Failed to start deployment.')
      setStarting(false)
    }
  }

  return (
    <div className="cicd-modal-backdrop" onMouseDown={onClose}>
      <div className="cicd-modal" onMouseDown={(e) => e.stopPropagation()}>
        <div className="cicd-modal-head">
          <h2>Deploy {website.websiteName}</h2>
          <button className="icon-btn" onClick={onClose}>
            ✕
          </button>
        </div>

        {error && <div className="alert alert-bad" style={{ marginBottom: '1rem' }}>{error}</div>}

        <div className="fields">
          <label>
            Branch
            <select value={branch} onChange={(e) => setBranch(e.target.value)} disabled={loadingBranches}>
              {loadingBranches && <option>Loading…</option>}
              {branches.map((b) => (
                <option key={b} value={b}>
                  {b}
                </option>
              ))}
            </select>
          </label>

          <div className="commit-box">
            {loadingCommit ? (
              <span className="muted">Loading latest commit…</span>
            ) : commit ? (
              <>
                <div>
                  <span className="mono" style={{ fontWeight: 700 }}>
                    {commit.shortSha}
                  </span>{' '}
                  · {commit.author}
                </div>
                <div className="muted small" style={{ marginTop: '0.25rem' }}>
                  {commit.message.split('\n')[0]}
                </div>
              </>
            ) : (
              <span className="muted">No commit info.</span>
            )}
          </div>
        </div>

        <div className="cicd-modal-actions">
          <button className="btn btn-ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={start} disabled={starting || !branch}>
            {starting ? 'Starting…' : 'Start Deployment'}
          </button>
        </div>
      </div>
    </div>
  )
}
