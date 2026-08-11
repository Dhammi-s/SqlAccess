import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { LoginPage } from './pages/LoginPage'
import { AgenciesPage } from './pages/AgenciesPage'
import { WebsitesPage } from './cicd/WebsitesPage'
import { SecurityLayout } from './security/SecurityLayout'
import { ApplicationsPage } from './security/ApplicationsPage'
import { SecretsPage } from './security/SecretsPage'
import { SecretAccessPage } from './security/SecretAccessPage'
import { AuditLogsPage } from './security/AuditLogsPage'
import { CacheLayout } from './cache/CacheLayout'
import { CacheDashboard } from './cache/CacheDashboard'
import { KeyExplorer } from './cache/KeyExplorer'
import { ClientsPage } from './cache/ClientsPage'
import { LogsPage } from './cache/LogsPage'
import { SettingsPage } from './cache/SettingsPage'

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route
            path="/agencies"
            element={
              <ProtectedRoute>
                <AgenciesPage />
              </ProtectedRoute>
            }
          />
          <Route
            path="/websites"
            element={
              <ProtectedRoute>
                <WebsitesPage />
              </ProtectedRoute>
            }
          />
          <Route
            path="/security"
            element={
              <ProtectedRoute>
                <SecurityLayout />
              </ProtectedRoute>
            }
          >
            <Route index element={<Navigate to="applications" replace />} />
            <Route path="applications" element={<ApplicationsPage />} />
            <Route path="secrets" element={<SecretsPage />} />
            <Route path="access" element={<SecretAccessPage />} />
            <Route path="audit" element={<AuditLogsPage />} />
          </Route>
          <Route
            path="/cache"
            element={
              <ProtectedRoute>
                <CacheLayout />
              </ProtectedRoute>
            }
          >
            <Route index element={<Navigate to="dashboard" replace />} />
            <Route path="dashboard" element={<CacheDashboard />} />
            <Route path="keys" element={<KeyExplorer />} />
            <Route path="clients" element={<ClientsPage />} />
            <Route path="logs" element={<LogsPage />} />
            <Route path="settings" element={<SettingsPage />} />
          </Route>
          <Route path="*" element={<Navigate to="/agencies" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}
