import { api } from './client'

export interface AppListItem {
  applicationId: number
  name: string
  clientId: string
  isActive: boolean
  createdOn: string
  secretCount: number
}
export interface RegisterAppResponse {
  applicationId: number
  name: string
  clientId: string
  clientSecret: string // returned once
}
export interface SecretListItem {
  secretId: number
  name: string
  secretType: string
  isActive: boolean
  currentVersion: number
  createdOn: string
  updatedOn?: string | null
}
export interface SecretVersionItem {
  secretVersionId: number
  version: number
  isCurrent: boolean
  createdOn: string
  createdBy?: string | null
}
export interface ApplicationSecretItem {
  applicationSecretId: number
  applicationId: number
  applicationName: string
  secretId: number
  secretName: string
  createdOn: string
}
export interface AuditLogItem {
  auditLogId: number
  applicationId?: number | null
  applicationName?: string | null
  secretId?: number | null
  secretName?: string | null
  action: string
  success: boolean
  ipAddress?: string | null
  detail?: string | null
  timestamp: string
}

export const SECRET_TYPES = [
  'ConnectionString',
  'JwtSecret',
  'Smtp',
  'OpenAI',
  'Anthropic',
  'Twilio',
  'Stripe',
  'Google',
  'Firebase',
  'Custom',
]

export const VaultApi = {
  // applications
  registerApplication: (name: string) =>
    api.post<RegisterAppResponse>('/vault/register-application', { name }).then((r) => r.data),
  listApplications: () => api.get<AppListItem[]>('/vault/applications').then((r) => r.data),

  // secrets
  listSecrets: (search?: string) =>
    api.get<SecretListItem[]>('/vault/secrets', { params: { search } }).then((r) => r.data),
  createSecret: (body: { name: string; secretType: string; value: string }) =>
    api.post<SecretListItem>('/vault/secrets', body).then((r) => r.data),
  updateSecret: (id: number, body: { value?: string; secretType?: string; isActive?: boolean }) =>
    api.put<SecretListItem>(`/vault/secrets/${id}`, body).then((r) => r.data),
  deleteSecret: (id: number) => api.delete(`/vault/secrets/${id}`).then((r) => r.data),
  rotateSecret: (secretId: number, newValue: string) =>
    api.post<SecretListItem>('/vault/rotate-secret', { secretId, newValue }).then((r) => r.data),
  getVersions: (secretId: number) =>
    api.get<SecretVersionItem[]>(`/vault/versions/${secretId}`).then((r) => r.data),
  restoreVersion: (secretId: number, version: number) =>
    api.post<SecretListItem>('/vault/restore-version', { secretId, version }).then((r) => r.data),

  // access
  assignSecret: (applicationId: number, secretId: number) =>
    api.post<ApplicationSecretItem>('/vault/assign-secret', { applicationId, secretId }).then((r) => r.data),
  listAssignments: () => api.get<ApplicationSecretItem[]>('/vault/assignments').then((r) => r.data),
  revoke: (applicationSecretId: number) =>
    api.delete(`/vault/assignments/${applicationSecretId}`).then((r) => r.data),

  // audit
  auditLogs: (take = 200) =>
    api.get<AuditLogItem[]>('/vault/auditlogs', { params: { take } }).then((r) => r.data),
}
