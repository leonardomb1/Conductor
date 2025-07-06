import { writable } from "svelte/store"
import { browser } from "$app/environment"

export interface User {
  id: number
  username: string
  email?: string
  role?: string
}

export interface LoginResponse {
  success: boolean
  token?: string
  user?: User
  message?: string
}

// Auth store
export const isAuthenticated = writable<boolean>(false)
export const currentUser = writable<User | null>(null)
export const authToken = writable<string | null>(null)

class AuthenticationService {
  private baseUrl = "/api"
  private tokenKey = "auth_token"
  private userKey = "current_user"

  constructor() {
    if (browser) {
      this.initializeAuth()
    }
  }

  private initializeAuth() {
    const token = localStorage.getItem(this.tokenKey)
    const userStr = localStorage.getItem(this.userKey)

    if (token && userStr) {
      try {
        const user = JSON.parse(userStr)
        authToken.set(token)
        currentUser.set(user)
        isAuthenticated.set(true)
      } catch (error) {
        console.error("Error parsing stored user data:", error)
        this.logout()
      }
    }
  }

  async login(username: string, password: string): Promise<LoginResponse> {
    try {
      const response = await fetch(`${this.baseUrl}/login`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ username, password }),
      })

      const data = await response.json()

      if (response.ok && data.token) {
        this.setAuthData(data.token, data.user)
        return { success: true, token: data.token, user: data.user }
      }

      return { success: false, message: data.message || "Login failed" }
    } catch (error) {
      console.error("Login error:", error)
      return { success: false, message: "Network error occurred" }
    }
  }

  async ssoLogin(token: string): Promise<LoginResponse> {
    try {
      const response = await fetch(`${this.baseUrl}/ssologin`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
      })

      const data = await response.json()

      if (response.ok && data.token) {
        this.setAuthData(data.token, data.user)
        return { success: true, token: data.token, user: data.user }
      }

      return { success: false, message: data.message || "SSO login failed" }
    } catch (error) {
      console.error("SSO login error:", error)
      return { success: false, message: "Network error occurred" }
    }
  }

  private setAuthData(token: string, user: User) {
    if (browser) {
      localStorage.setItem(this.tokenKey, token)
      localStorage.setItem(this.userKey, JSON.stringify(user))
    }

    authToken.set(token)
    currentUser.set(user)
    isAuthenticated.set(true)
  }

  logout() {
    if (browser) {
      localStorage.removeItem(this.tokenKey)
      localStorage.removeItem(this.userKey)
    }

    authToken.set(null)
    currentUser.set(null)
    isAuthenticated.set(false)
  }

  async authenticatedFetch(url: string, options: RequestInit = {}): Promise<Response> {
    const token = browser ? localStorage.getItem(this.tokenKey) : null

    if (!token) {
      this.logout()
      throw new Error("No authentication token available")
    }

    const headers = {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
      ...options.headers,
    }

    try {
      const response = await fetch(url, {
        ...options,
        headers,
      })

      if (response.status === 401) {
        this.logout()
        if (browser) {
          window.location.href = "/"
        }
        throw new Error("Authentication expired")
      }

      return response
    } catch (error) {
      if (error instanceof Error && error.message === "Authentication expired") {
        throw error
      }
      console.error("Authenticated fetch error:", error)
      throw error
    }
  }

  getToken(): string | null {
    return browser ? localStorage.getItem(this.tokenKey) : null
  }

  isLoggedIn(): boolean {
    return !!this.getToken()
  }
}

export const AuthService = new AuthenticationService()
