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
          <Route path="*" element={<Navigate to="/agencies" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}
