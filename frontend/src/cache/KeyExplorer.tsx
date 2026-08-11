import { useCallback, useEffect, useState } from 'react'
import { CacheApi, type KeyInfo, type PagedKeys } from '../api/cache'

function ttlText(t: number) {
  if (t === -1) return 'no expiry'
  if (t < 0) return 'expired'
  return `${t}s`
}

export function KeyExplorer() {
  const [data, setData] = useState<PagedKeys | null>(null)
  const [pattern, setPattern] = useState('')
  const [page, setPage] = useState(1)
  const pageSize = 25
  const [loading, setLoading] = useState(true)
  const [viewing, setViewing] = useState<{ key: string; value: string | null } | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    CacheApi.keys(pattern || undefined, page, pageSize)
      .then(setData)
      .finally(() => setLoading(false))
  }, [pattern, page])

  useEffect(() => {
    load()
  }, [load])

  async function view(k: KeyInfo) {
    const res = await CacheApi.get(k.key)
    setViewing({ key: k.key, value: res.value ?? null })
  }
  async function del(k: KeyInfo) {
    if (!confirm(`Delete key "${k.key}"?`)) return
    await CacheApi.del(k.key)
    load()
  }
  async function setTtl(k: KeyInfo) {
    const input = prompt(`TTL (seconds) for "${k.key}":`, '60')
    if (input == null) return
    await CacheApi.expire(k.key, parseInt(input) || 0)
    load()
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1
  const prettyValue = (v: string) => {
    try {
      return JSON.stringify(JSON.parse(v), null, 2)
    } catch {
      return v
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Key Explorer</h1>
          <p className="muted small">{data ? `${data.total} keys` : '—'}</p>
        </div>
        <button className="btn btn-ghost" onClick={load}>
          Refresh
        </button>
      </div>

      <div className="toolbar">
        <input
          className="search"
          placeholder="Search keys…"
          value={pattern}
          onChange={(e) => setPattern(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              setPage(1)
              load()
            }
          }}
        />
        <button
          className="btn btn-ghost sm"
          onClick={() => {
            setPage(1)
            load()
          }}
        >
          Search
        </button>
      </div>

      <div className="card table-card">
        <table className="tbl">
          <thead>
            <tr>
              <th>Key</th>
              <th>Type</th>
              <th>Size</th>
              <th>TTL</th>
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
            ) : (data?.items ?? []).length === 0 ? (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center' }}>
                  No keys.
                </td>
              </tr>
            ) : (
              data!.items.map((k) => (
                <tr key={k.key}>
                  <td className="mono cell-title">{k.key}</td>
                  <td>
                    <span className="type-chip">string</span>
                  </td>
                  <td>{k.sizeBytes} B</td>
                  <td className="mono">{ttlText(k.ttlSeconds)}</td>
                  <td className="right">
                    <div className="row-actions">
                      <button className="btn btn-ghost xs" onClick={() => view(k)}>
                        View
                      </button>
                      <button className="btn btn-ghost xs" onClick={() => setTtl(k)}>
                        TTL
                      </button>
                      <button className="btn btn-danger xs" onClick={() => del(k)}>
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

      <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center', marginTop: '1rem' }}>
        <button className="btn btn-ghost sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
          ← Prev
        </button>
        <span className="muted small">
          Page {page} / {totalPages}
        </span>
        <button className="btn btn-ghost sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
          Next →
        </button>
      </div>

      {viewing && (
        <div className="cicd-modal-backdrop" onMouseDown={() => setViewing(null)}>
          <div className="cicd-modal wide" onMouseDown={(e) => e.stopPropagation()}>
            <div className="cicd-modal-head">
              <h2 className="mono">{viewing.key}</h2>
              <button className="icon-btn" onClick={() => setViewing(null)}>
                ✕
              </button>
            </div>
            <pre className="value-view">{viewing.value == null ? '(nil)' : prettyValue(viewing.value)}</pre>
            <div className="cicd-modal-actions">
              <span />
              <button className="btn btn-primary" onClick={() => setViewing(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
