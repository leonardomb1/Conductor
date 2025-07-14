import type { AuthStore } from './types.js';
import { goto } from '$app/navigation';

class AuthManager {
  private store: AuthStore = $state({
    isAuthenticated: false,
    token: null,
    user: null
  });

  constructor() {
    // Load from localStorage on initialization
    if (typeof window !== 'undefined') {
      const token = localStorage.getItem('conductor_token');
      const user = localStorage.getItem('conductor_user');
      
      if (token && user) {
        this.store.token = token;
        this.store.user = user;
        this.store.isAuthenticated = true;
      }
    }
  }

  get isAuthenticated() {
    return this.store.isAuthenticated;
  }

  get user() {
    return this.store.user;
  }

  get token() {
    return this.store.token;
  }

  async login(username: string, password: string, useLdap = false): Promise<boolean> {
    try {
      const endpoint = useLdap ? '/api/ssologin' : '/api/login';
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      });

      if (!response.ok) {
        throw new Error(`Login failed: ${response.status}`);
      }

      const token = await response.text();
      
      // Update state
      this.store.token = token;
      this.store.user = username;
      this.store.isAuthenticated = true;
      
      // Store in localStorage
      localStorage.setItem('conductor_token', token);
      localStorage.setItem('conductor_user', username);
      
      return true;
    } catch (error) {
      this.logout();
      return false;
    }
  }

  logout() {
    this.store.isAuthenticated = false;
    this.store.token = null;
    this.store.user = null;
    
    localStorage.removeItem('conductor_token');
    localStorage.removeItem('conductor_user');
  }

  // Handle unauthorized responses from API
  handleUnauthorized() {
    this.logout();
    goto('/login');
  }
}

export const auth = new AuthManager();