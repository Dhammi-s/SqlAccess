import { api } from './client'

export interface AgencyListItem {
  agencyId: number
  agencyName: string
  domainUrl?: string | null
  location?: string | null
  dbServer?: string | null
  dbName?: string | null
  dbUser?: string | null
  passwordMasked: string
  isActive: boolean
  isArchived: boolean
  createdOn: string
  updatedOn?: string | null
}

export interface AgencyDetail {
  agencyId: number
  agencyName: string
  domainUrl?: string | null
  location?: string | null
  dbServer?: string | null
  dbName?: string | null
  dbUser?: string | null
  dbPassword?: string | null
  connectionString?: string | null
  isActive: boolean
  isArchived: boolean
  createdOn: string
  updatedOn?: string | null
}

export interface AgencyUpsert {
  agencyName: string
  domainUrl?: string | null
  location?: string | null
  dbServer?: string | null
  dbName?: string | null
  dbUser?: string | null
  dbPassword?: string | null
  connectionString?: string | null
  isActive: boolean
}

export interface TestConnectionResult {
  success: boolean
  message: string
  elapsedMs: number
}

export interface DbRole {
  roleName: string
  isFixedRole: boolean
  typeDesc: string
  memberCount: number
  members?: string | null
}

export interface DbRolesResult {
  success: boolean
  message: string
  totalRoles: number
  roles: DbRole[]
}

export interface CreateRoleResult {
  success: boolean
  message: string
}

export const AgenciesApi = {
  list: (includeArchived = false) =>
    api.get<AgencyListItem[]>('/agencies', { params: { includeArchived } }).then((r) => r.data),

  get: (id: number) => api.get<AgencyDetail>(`/agencies/${id}`).then((r) => r.data),

  create: (body: AgencyUpsert) => api.post<AgencyDetail>('/agencies', body).then((r) => r.data),

  update: (id: number, body: AgencyUpsert) =>
    api.put<AgencyDetail>(`/agencies/${id}`, body).then((r) => r.data),

  archive: (id: number, archived = true) =>
    api.delete(`/agencies/${id}`, { params: { archived } }).then((r) => r.data),

  test: (id: number) =>
    api.post<TestConnectionResult>(`/agencies/${id}/test`).then((r) => r.data),

  testAdHoc: (connectionString: string) =>
    api.post<TestConnectionResult>('/agencies/test', { connectionString }).then((r) => r.data),

  roles: (id: number) => api.get<DbRolesResult>(`/agencies/${id}/roles`).then((r) => r.data),

  createRole: (id: number, roleName: string, readOnly: boolean) =>
    api.post<CreateRoleResult>(`/agencies/${id}/roles`, { roleName, readOnly }).then((r) => r.data),
}
