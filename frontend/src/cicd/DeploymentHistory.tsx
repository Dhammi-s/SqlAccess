import { useEffect, useState } from 'react'
import { DeploymentsApi, type DeploymentListItem, type WebsiteListItem } from '../api/cicd'
import { StatusBadge } from './StatusBadge'

export function DeploymentHistory({
  website,
  onClose,
  onOpenConsole,
}: {
  website: WebsiteListItem
  onClose: () => void
  onOpenConsole: (deploymentId: number) => void
}) {
  const [rows, setRows] = useState<DeploymentListItem[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    DeploymentsApi.list(website.websiteId, 100)
      .then(setRows)
      .finally(() => setLoading(false))
  }, [website.websiteId])

  return (
    <div className="cicd-modal-backdrop" onMouseDown={onClose}>
      <div className="cicd-modal wide" onMouseDown={(e) => e.stopPropagation()}>
        <div className="cicd-modal-head">
          <h2>{website.websiteName} — deployment history</h2>
          <button className="icon-btn" onClick={onClose}>
            ✕
          </button>
        </div>

        <div className="card table-card">
          <table className="tbl">
            <thead>
              <tr>
                <th>#</th>
                <th>Branch</th>
                <th>Commit</th>
                <th>Status</th>
                <th>Duration</th>
                <th>By</th>
                <th>Started</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={8} className="muted" style={{ textAlign: 'center' }}>
                    Loading…
                  </td>
                </tr>
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={8} className="muted" style={{ textAlign: 'center' }}>
                    No deployments yet.
                  </td>
                </tr>
              ) : (
                rows.map((d) => (
                  <tr key={d.deploymentId}>
                    <td>{d.deploymentId}</td>
                    <td className="mono">{d.branch}</td>
                    <td className="mono">{d.commitId ? d.commitId.slice(0, 7) : '—'}</td>
                    <td>
                      <StatusBadge status={d.status} />
                    </td>
                    <td>{d.durationSeconds != null ? `${Math.round(d.durationSeconds)}s` : '—'}</td>
                    <td className="small">{d.triggeredBy}</td>
                    <td className="small">{d.startedOn ? new Date(d.startedOn).toLocaleString() : '—'}</td>
                    <td>
                      <button className="btn btn-ghost xs" onClick={() => onOpenConsole(d.deploymentId)}>
                        Logs
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <div className="cicd-modal-actions">
          <span />
          <button className="btn btn-primary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
