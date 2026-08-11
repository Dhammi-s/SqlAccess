import { useEffect, useState } from 'react'
import { CacheApi, type ClientInfo } from '../api/cache'

export function ClientsPage() {
  const [clients, setClients] = useState<ClientInfo[]>([])
  const [loading, setLoading] = useState(true)

  function load() {
    setLoading(true)
    CacheApi.clients()
      .then(setClients)
      .finally(() => setLoading(false))
  }
  useEffect(() => {
    load()
    const t = setInterval(load, 3000)
    return () => clearInterval(t)
  }, [])

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Connected Clients</h1>
          <p className="muted small">{clients.length} connected (TCP)</p>
        </div>
        <button className="btn btn-ghost" onClick={load}>
          Refresh
        </button>
      </div>

      <div className="card table-card">
        <table className="tbl">
          <thead>
            <tr>
              <th>ID</th>
              <th>Remote Endpoint</th>
              <th>Connected</th>
              <th className="right" style={{ textAlign: 'right' }}>
                Commands
              </th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={4} className="muted" style={{ textAlign: 'center' }}>
                  Loading…
                </td>
              </tr>
            ) : clients.length === 0 ? (
              <tr>
                <td colSpan={4} className="muted" style={{ textAlign: 'center' }}>
                  No TCP clients connected.
                </td>
              </tr>
            ) : (
              clients.map((c) => (
                <tr key={c.id}>
                  <td className="mono">{c.id}</td>
                  <td className="mono">{c.remoteEndpoint}</td>
                  <td className="small">{new Date(c.connectedAtUtc).toLocaleString()}</td>
                  <td className="right" style={{ textAlign: 'right' }}>
                    {c.commandsProcessed.toLocaleString()}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </>
  )
}
