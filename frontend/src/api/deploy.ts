import { api, getToken, API_BASE } from './client'

export interface DacpacInfo {
  dacpacId: string
  fileName: string
  sizeBytes: number
  uploadedOn: string
}

export interface DeployResult {
  agencyId: number
  agencyName: string
  targetServer: string
  targetDatabase: string
  success: boolean
  message: string
  elapsedMs: number
  scriptGenerated: boolean
  script?: string | null
}

export interface BranchInfo {
  name: string
}

export interface BuildResult {
  success: boolean
  message: string
  dacpac?: DacpacInfo | null
  modelFileCount: number
  warnings: number
  errors: string[]
  emailSent: boolean
}

export interface DeployRunRequest {
  dacpacId: string
  agencyId: number
  generateScriptOnly: boolean
  blockOnPossibleDataLoss: boolean
  dropObjectsNotInSource: boolean
}

export const DeployApi = {
  // Uses fetch so the browser sets the multipart boundary itself (avoids axios Content-Type pitfalls).
  upload: async (file: File): Promise<DacpacInfo> => {
    const fd = new FormData()
    fd.append('file', file)
    const res = await fetch(`${API_BASE}/deploy/upload`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${getToken() ?? ''}` },
      body: fd,
    })
    if (!res.ok) {
      let msg = 'Upload failed.'
      try {
        msg = (await res.json()).message ?? msg
      } catch {
        /* ignore */
      }
      throw new Error(msg)
    }
    return res.json()
  },

  run: (req: DeployRunRequest) => api.post<DeployResult>('/deploy/run', req).then((r) => r.data),

  branches: () => api.get<BranchInfo[]>('/deploy/branches').then((r) => r.data),

  build: (branch: string) => api.post<BuildResult>('/deploy/build', { branch }).then((r) => r.data),
}
