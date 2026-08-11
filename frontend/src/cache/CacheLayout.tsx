import { Link, NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import '../cicd/cicd.css'
import '../security/security.css'
import './cache.css'

const TABS = [
  { to: '/cache/dashboard', label: 'Dashboard' },
  { to: '/cache/keys', label: 'Key Explorer' },
  { to: '/cache/clients', label: 'Clients' },
  { to: '/cache/logs', label: 'Logs' },
  { to: '/cache/settings', label: 'Settings' },
]

export function CacheLayout() {
  const { username, logout } = useAuth()
  return (
    <div className="cicd">
      <header className="topbar">
        <div className="brand-inline">
          <div className="brand-mark">⚡</div>
          In-Memory Cache
        </div>
        <nav className="nav">
          <Link to="/agencies">Agencies</Link>
          <Link to="/websites">Deployments</Link>
          <Link to="/security/applications">Security</Link>
          <Link to="/cache/dashboard" className="active">
            Cache
          </Link>
        </nav>
        <div style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
          <span className="muted small">{username}</span>
          <button className="btn btn-ghost sm" onClick={logout}>
            Sign out
          </button>
        </div>
      </header>

      <div className="sec-subnav">
        <div className="sec-subnav-inner">
          {TABS.map((t) => (
            <NavLink key={t.to} to={t.to} className={({ isActive }) => `sec-tab ${isActive ? 'active' : ''}`}>
              {t.label}
            </NavLink>
          ))}
        </div>
      </div>

      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
