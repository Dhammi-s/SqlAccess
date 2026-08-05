import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { DeploymentsApi, HUB_URL, hubAccessToken, type DeploymentListItem, type LogEntry } from '../api/cicd'
import { StatusBadge } from './StatusBadge'

interface Line {
  ts: string
  type: string
  message: string
}

const FINISHED = ['Success', 'Failed', 'Cancelled']

export function DeployConsole({
  deploymentId,
  onClose,
  onChanged,
}: {
  deploymentId: number
  onClose: () => void
  onChanged?: () => void
}) {
  const [lines, setLines] = useState<Line[]>([])
  const [status, setStatus] = useState<string>('Queued')
  const [progress, setProgress] = useState<number>(0)
  const [info, setInfo] = useState<DeploymentListItem | null>(null)
  const [busy, setBusy] = useState(false)
  const consoleRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let conn: signalR.HubConnection | null = null
    let disposed = false

    async function init() {
      // Catch up on any logs already recorded.
      try {
        const [existing, d] = await Promise.all([
          DeploymentsApi.logs(deploymentId),
          DeploymentsApi.get(deploymentId),
        ])
        if (disposed) return
        setLines(existing.map((l: LogEntry) => ({ ts: l.timestamp, type: l.logType, message: l.message ?? '' })))
        if (d) {
          setInfo(d)
          setStatus(d.status)
        }
      } catch {
        /* ignore */
      }

      conn = new signalR.HubConnectionBuilder()
        .withUrl(HUB_URL, { accessTokenFactory: () => hubAccessToken() })
        .withAutomaticReconnect()
        .build()

      conn.on('log', (l: { timestamp: string; logType: string; message: string }) =>
        setLines((prev) => [...prev, { ts: l.timestamp, type: l.logType, message: l.message }]),
      )
      conn.on('progress', (p: { percent: number }) => setProgress(p.percent))
      conn.on('status', (s: { status: string }) => {
        setStatus(s.status)
        if (FINISHED.includes(s.status)) onChanged?.()
      })

      try {
        await conn.start()
        await conn.invoke('JoinDeployment', deploymentId)
      } catch {
        /* connection will retry */
      }
    }

    init()
    return () => {
      disposed = true
      conn?.stop()
    }
  }, [deploymentId, onChanged])

  useEffect(() => {
    consoleRef.current?.scrollTo({ top: consoleRef.current.scrollHeight })
  }, [lines])

  const running = !FINISHED.includes(status)

  async function cancel() {
    setBusy(true)
    try {
      await DeploymentsApi.cancel(deploymentId)
    } finally {
      setBusy(false)
    }
  }

  async function retry() {
    setBusy(true)
    try {
      await DeploymentsApi.retry(deploymentId)
      onChanged?.()
      onClose()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="cicd-modal-backdrop" onMouseDown={onClose}>
      <div className="cicd-modal wide" onMouseDown={(e) => e.stopPropagation()}>
        <div className="cicd-modal-head">
          <div>
            <h2>Deployment #{deploymentId}</h2>
            <div className="muted small">
              {info?.websiteName} · {info?.branch} {info?.commitId ? `· ${info.commitId.slice(0, 7)}` : ''}
            </div>
          </div>
          <div style={{ display: 'flex', gap: '0.6rem', alignItems: 'center' }}>
            <StatusBadge status={status} />
            <button className="icon-btn" onClick={onClose}>
              ✕
            </button>
          </div>
        </div>

        {running && (
          <div className="progress">
            <span style={{ width: `${progress}%` }} />
          </div>
        )}

        <div className="console" ref={consoleRef}>
          {lines.length === 0 && <div className="ln muted">Waiting for logs…</div>}
          {lines.map((l, i) => (
            <div key={i} className="ln">
              <span className="ts">{new Date(l.ts).toLocaleTimeString()}</span>
              <span className={l.type}>{l.message}</span>
            </div>
          ))}
        </div>

        <div className="cicd-modal-actions">
          <span className="muted small">You can close this — the deployment keeps running.</span>
          <div style={{ display: 'flex', gap: '0.6rem' }}>
            {running ? (
              <button className="btn btn-danger" onClick={cancel} disabled={busy}>
                Cancel deployment
              </button>
            ) : (
              <button className="btn btn-ghost" onClick={retry} disabled={busy}>
                Retry
              </button>
            )}
            <button className="btn btn-primary" onClick={onClose}>
              Close
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
