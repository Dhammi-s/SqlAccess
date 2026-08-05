import { api, API_BASE, getToken } from './client'

// SignalR hub lives next to the API. In dev API_BASE='/api' (proxied); in prod it's the runasp.net origin.
export const HUB_URL = `${API_BASE.replace(/\/api$/, '')}/hubs/deployment`

export interface DeploymentBrief {
  deploymentId: number
  status: string
  branch?: string | null
  commitId?: string | null
  startedOn?: string | null
  finishedOn?: string | null
}

export interface WebsiteListItem {
  websiteId: number
  websiteName: string
  repositoryUrl?: string | null
  gitProvider: string
  defaultBranch?: string | null
  projectType: string
  ftpHost?: string | null
  isActive: boolean
  createdOn: string
  updatedOn?: string | null
  lastDeployment?: DeploymentBrief | null
}

export interface WebsiteDetail {
  websiteId: number
  websiteName: string
  repositoryUrl?: string | null
  gitProvider: string
  defaultBranch?: string | null
  projectType: string
  buildCommand?: string | null
  publishCommand?: string | null
  publishFolder?: string | null
  deployProvider: string
  workflowFile?: string | null
  ftpHost?: string | null
  ftpPort: number
  ftpUsername?: string | null
  ftpRootFolder?: string | null
  isActive: boolean
  hasGitPat: boolean
  hasFtpPassword: boolean
  createdOn: string
  updatedOn?: string | null
}

export interface UpsertWebsite {
  websiteName: string
  repositoryUrl?: string | null
  gitProvider: string
  defaultBranch?: string | null
  projectType: string
  gitPat?: string | null
  buildCommand?: string | null
  publishCommand?: string | null
  publishFolder?: string | null
  deployProvider: string
  workflowFile?: string | null
  ftpHost?: string | null
  ftpPort: number
  ftpUsername?: string | null
  ftpPassword?: string | null
  ftpRootFolder?: string | null
  isActive: boolean
}

export interface BranchInfo {
  name: string
}
export interface CommitInfo {
  sha: string
  shortSha: string
  message: string
  author: string
  date?: string | null
}
export interface TestResult {
  success: boolean
  message: string
}
export interface BuildTemplate {
  projectType: string
  buildCommand: string
  publishCommand: string
  publishFolder: string
}
export interface DeploymentListItem {
  deploymentId: number
  websiteId: number
  websiteName?: string | null
  branch?: string | null
  commitId?: string | null
  commitMessage?: string | null
  triggeredBy?: string | null
  status: string
  startedOn?: string | null
  finishedOn?: string | null
  durationSeconds?: number | null
}
export interface LogEntry {
  logId: number
  timestamp: string
  logType: string
  message?: string | null
}

export const WebsitesApi = {
  list: () => api.get<WebsiteListItem[]>('/websites').then((r) => r.data),
  get: (id: number) => api.get<WebsiteDetail>(`/websites/${id}`).then((r) => r.data),
  create: (body: UpsertWebsite) => api.post<WebsiteDetail>('/websites', body).then((r) => r.data),
  update: (id: number, body: UpsertWebsite) => api.put<WebsiteDetail>(`/websites/${id}`, body).then((r) => r.data),
  remove: (id: number) => api.delete(`/websites/${id}`).then((r) => r.data),
  branches: (id: number) => api.get<BranchInfo[]>(`/websites/${id}/branches`).then((r) => r.data),
  latestCommit: (id: number, branch: string) =>
    api.get<CommitInfo>(`/websites/${id}/commit`, { params: { branch } }).then((r) => r.data),
  testGit: (repositoryUrl: string, pat?: string | null) =>
    api.post<TestResult>('/websites/test-git', { repositoryUrl, pat }).then((r) => r.data),
  previewBranches: (repositoryUrl: string, pat?: string | null) =>
    api.post<BranchInfo[]>('/websites/branches-preview', { repositoryUrl, pat }).then((r) => r.data),
  testFtp: (body: {
    host: string
    port: number
    username: string
    password?: string | null
    rootFolder?: string | null
    provider?: string
  }) => api.post<TestResult>('/websites/test-ftp', body).then((r) => r.data),
  buildTemplates: () => api.get<BuildTemplate[]>('/websites/build-templates').then((r) => r.data),
}

export const DeploymentsApi = {
  trigger: (websiteId: number, branch: string) =>
    api.post<{ deploymentId: number }>('/deployments', { websiteId, branch }).then((r) => r.data),
  retry: (id: number) => api.post<{ deploymentId: number }>(`/deployments/${id}/retry`).then((r) => r.data),
  cancel: (id: number) => api.post(`/deployments/${id}/cancel`).then((r) => r.data),
  list: (websiteId?: number, take = 50) =>
    api.get<DeploymentListItem[]>('/deployments', { params: { websiteId, take } }).then((r) => r.data),
  get: (id: number) => api.get<DeploymentListItem>(`/deployments/${id}`).then((r) => r.data),
  logs: (id: number, after = 0) =>
    api.get<LogEntry[]>(`/deployments/${id}/logs`, { params: { after } }).then((r) => r.data),
}

export function hubAccessToken() {
  return getToken() ?? ''
}
