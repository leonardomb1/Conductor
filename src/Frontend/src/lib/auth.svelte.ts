import type { AuthStore } from './types.js';
import { api } from './api.js';

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
        api.setToken(token);
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

  async login(username: string, password: string, useLdap = false) {
    try {
      const token = useLdap 
        ? await api.loginLdap(username, password)
        : await api.login(username, password);
      
      this.store.token = token;
      this.store.user = username;
      this.store.isAuthenticated = true;
      
      api.setToken(token);
      
      if (typeof window !== 'undefined') {
        localStorage.setItem('conductor_token', token);
        localStorage.setItem('conductor_user', username);
      }
      
      return true;
    } catch (error) {
      console.error('Login failed:', error);
      return false;
    }
  }

  logout() {
    this.store.isAuthenticated = false;
    this.store.token = null;
    this.store.user = null;
    
    api.setToken(null);
    
    if (typeof window !== 'undefined') {
      localStorage.removeItem('conductor_token');
      localStorage.removeItem('conductor_user');
    }
  }
}

export const auth = new AuthManager();
