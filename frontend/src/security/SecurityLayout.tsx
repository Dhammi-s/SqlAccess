import { Link, NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import '../cicd/cicd.css'
import './security.css'

const TABS = [
  { to: '/security/applications', label: 'Applications' },
  { to: '/security/secrets', label: 'Secrets' },
  { to: '/security/access', label: 'Secret Access' },
  { to: '/security/audit', label: 'Audit Logs' },
]

export function SecurityLayout() {
  const { username, logout } = useAuth()
  return (
    <div className="cicd">
      <header className="topbar">
        <div className="brand-inline">
          <div className="brand-mark">🔐</div>
          Secret Vault
        </div>
        <nav className="nav">
          <Link to="/agencies">Agencies</Link>
          <Link to="/websites">Deployments</Link>
          <Link to="/security/applications" className="active">
            Security
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
            <NavLink
              key={t.to}
              to={t.to}
              className={({ isActive }) => `sec-tab ${isActive ? 'active' : ''}`}
            >
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
