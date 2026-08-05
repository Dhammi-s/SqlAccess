import { createContext, useContext, useState, type ReactNode } from 'react'
import { api, getToken, setToken } from '../api/client'

interface AuthState {
  username: string | null
  isAuthenticated: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthState | undefined>(undefined)

const USER_KEY = 'sqlaccess.user'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [username, setUsername] = useState<string | null>(
    () => (getToken() ? localStorage.getItem(USER_KEY) : null),
  )

  async function login(user: string, password: string) {
    const { data } = await api.post('/auth/login', { username: user, password })
    setToken(data.token)
    localStorage.setItem(USER_KEY, data.username)
    setUsername(data.username)
  }

  function logout() {
    setToken(null)
    localStorage.removeItem(USER_KEY)
    setUsername(null)
  }

  return (
    <AuthContext.Provider value={{ username, isAuthenticated: !!username, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
