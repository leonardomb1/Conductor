// src/Frontend/src/lib/api.ts
import type { ApiResponse, Destination, Origin, Schedule, User, Extraction, JobDto, ExtractionAggregatedDto } from './types.js';
import { auth } from './auth.svelte.js';

class ApiClient {
  private async request<T>(endpoint: string, options: RequestInit = {}): Promise<ApiResponse<T>> {
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
      ...options.headers,
    };

    // Add Bearer token if available
    if (auth.token) {
      headers.Authorization = `Bearer ${auth.token}`;
    } 

    try {
      const response = await fetch(`/api${endpoint}`, {
        ...options,
        headers,
      });

      // Handle 401 Unauthorized
      if (response.status === 401) {
        auth.handleUnauthorized();
        throw new Error('Session expired - please login again');
      }

      if (!response.ok) {
        let errorMessage = `API request failed: ${response.status} ${response.statusText}`;
        try {
          const errorText = await response.text();
          if (errorText) {
            errorMessage += ` - ${errorText}`;
          }
        } catch (e) {
          // Ignore parsing errors
        }
        throw new Error(errorMessage);
      }

      return response.json();
    } catch (error) {
      if (error instanceof TypeError && error.message.includes('fetch')) {
        throw new Error('Network error - please check your connection');
      }
      throw error;
    }
  }

  // Login methods (don't use request method to avoid circular dependencies)
  async login(username: string, password: string): Promise<string> {
    const response = await fetch('/api/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });

    if (!response.ok) {
      throw new Error('Invalid username or password');
    }

    const token = await response.text();
    return token;
  }

  async loginLdap(username: string, password: string): Promise<string> {
    const response = await fetch('/api/ssologin', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });

    if (!response.ok) {
      throw new Error('LDAP authentication failed');
    }

    const token = await response.text();
    return token;
  }

  // Test endpoint to verify auth is working
  async testAuth(): Promise<{ authenticated: boolean; user?: string }> {
    try {
      const health = await this.getHealth();
      return { authenticated: true, user: auth.user || undefined };
    } catch (error) {
      return { authenticated: false };
    }
  }

  // Health check (simple, no auth loops)
  async getHealth(): Promise<{ status: string; timestamp: string; activeJobs: number }> {
    const headers: HeadersInit = {};
    if (auth.token) {
      headers.Authorization = `Bearer ${auth.token}`;
    } 

    const response = await fetch('/api/health', { headers });
    
    if (response.status === 401) {
      throw new Error('Unauthorized');
    }
    
    if (!response.ok) {
      throw new Error('Failed to fetch health data');
    }
    
    return response.json();
  }

  async getMetrics(): Promise<any> {
    const headers: HeadersInit = {};
    if (auth.token) {
      headers.Authorization = `Bearer ${auth.token}`;
    }

    const response = await fetch('/api/metrics/json', { headers });
    
    if (response.status === 401) {
      throw new Error('Unauthorized');
    }
    
    if (!response.ok) {
      throw new Error('Failed to fetch metrics data');
    }
    
    return response.json();
  }

  // Helper method to build query params with defaults
  private buildFilters(filters?: Record<string, string>, defaultTake = 20): Record<string, string> {
    const params: Record<string, string> = {
      take: defaultTake.toString(),
      ...filters
    };

    // Ensure skip is set if not provided
    if (!params.skip) {
      params.skip = '0';
    }

    return params;
  }

  // Destinations
  async getDestinations(filters?: Record<string, string>): Promise<ApiResponse<Destination>> {
    const params = this.buildFilters(filters);
    const queryString = new URLSearchParams(params).toString();
    return this.request<Destination>(`/destinations?${queryString}`);
  }

  async getDestination(id: number): Promise<ApiResponse<Destination>> {
    return this.request<Destination>(`/destinations/${id}`);
  }

  async createDestination(destination: Omit<Destination, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>('/destinations', {
      method: 'POST',
      body: JSON.stringify(destination),
    });
  }

  async updateDestination(id: number, destination: Omit<Destination, 'id'>): Promise<void> {
    await this.request(`/destinations/${id}`, {
      method: 'PUT',
      body: JSON.stringify(destination),
    });
  }

  async deleteDestination(id: number): Promise<void> {
    await this.request(`/destinations/${id}`, {
      method: 'DELETE',
    });
  }

  // Origins
  async getOrigins(filters?: Record<string, string>): Promise<ApiResponse<Origin>> {
    const params = this.buildFilters(filters);
    const queryString = new URLSearchParams(params).toString();
    return this.request<Origin>(`/origins?${queryString}`);
  }

  async getOrigin(id: number): Promise<ApiResponse<Origin>> {
    return this.request<Origin>(`/origins/${id}`);
  }

  async createOrigin(origin: Omit<Origin, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>('/origins', {
      method: 'POST',
      body: JSON.stringify(origin),
    });
  }

  async updateOrigin(id: number, origin: Omit<Origin, 'id'>): Promise<void> {
    await this.request(`/origins/${id}`, {
      method: 'PUT',
      body: JSON.stringify(origin),
    });
  }

  async deleteOrigin(id: number): Promise<void> {
    await this.request(`/origins/${id}`, {
      method: 'DELETE',
    });
  }

  // Schedules
  async getSchedules(filters?: Record<string, string>): Promise<ApiResponse<Schedule>> {
    const params = this.buildFilters(filters);
    const queryString = new URLSearchParams(params).toString();
    return this.request<Schedule>(`/schedules?${queryString}`);
  }

  async getSchedule(id: number): Promise<ApiResponse<Schedule>> {
    return this.request<Schedule>(`/schedules/${id}`);
  }

  async createSchedule(schedule: Omit<Schedule, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>('/schedules', {
      method: 'POST',
      body: JSON.stringify(schedule),
    });
  }

  async updateSchedule(id: number, schedule: Omit<Schedule, 'id'>): Promise<void> {
    await this.request(`/schedules/${id}`, {
      method: 'PUT',
      body: JSON.stringify(schedule),
    });
  }

  async deleteSchedule(id: number): Promise<void> {
    await this.request(`/schedules/${id}`, {
      method: 'DELETE',
    });
  }

  // Users
  async getUsers(filters?: Record<string, string>): Promise<ApiResponse<User>> {
    const params = this.buildFilters(filters);
    const queryString = new URLSearchParams(params).toString();
    return this.request<User>(`/users?${queryString}`);
  }

  async getUser(id: number): Promise<ApiResponse<User>> {
    return this.request<User>(`/users/${id}`);
  }

  async createUser(user: Omit<User, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>('/users', {
      method: 'POST',
      body: JSON.stringify(user),
    });
  }

  async updateUser(id: number, user: Omit<User, 'id'>): Promise<void> {
    await this.request(`/users/${id}`, {
      method: 'PUT',
      body: JSON.stringify(user),
    });
  }

  async deleteUser(id: number): Promise<void> {
    await this.request(`/users/${id}`, {
      method: 'DELETE',
    });
  }

  // Extractions
  async getExtractions(filters?: Record<string, string>): Promise<ApiResponse<Extraction>> {
    const params = this.buildFilters(filters);
    const queryString = new URLSearchParams(params).toString();
    return this.request<Extraction>(`/extractions?${queryString}`);
  }

  async getExtraction(id: number): Promise<ApiResponse<Extraction>> {
    return this.request<Extraction>(`/extractions/${id}`);
  }

  async createExtraction(extraction: Omit<Extraction, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>('/extractions', {
      method: 'POST',
      body: JSON.stringify(extraction),
    });
  }

  async updateExtraction(id: number, extraction: Omit<Extraction, 'id'>): Promise<void> {
    await this.request(`/extractions/${id}`, {
      method: 'PUT',
      body: JSON.stringify(extraction),
    });
  }

  async deleteExtraction(id: number): Promise<void> {
    await this.request(`/extractions/${id}`, {
      method: 'DELETE',
    });
  }

  async executeTransfer(filters?: Record<string, string>): Promise<ApiResponse<never>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<never>(`/extractions/transfer${params}`, {
      method: 'POST',
    });
  }

  async executePull(filters?: Record<string, string>): Promise<ApiResponse<never>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<never>(`/extractions/pull${params}`, {
      method: 'POST',
    });
  }

  async fetchData(filters?: Record<string, string>): Promise<ApiResponse<Record<string, any>>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<Record<string, any>>(`/extractions/fetch${params}`);
  }

  // Jobs
  async getActiveJobs(): Promise<ApiResponse<JobDto>> {
    return this.request<JobDto>('/jobs/active');
  }

  async searchJobs(filters?: Record<string, string>): Promise<ApiResponse<JobDto>> {
    const params = this.buildFilters(filters);
    const queryString = new URLSearchParams(params).toString();
    return this.request<JobDto>(`/jobs/search?${queryString}`);
  }

  async getAggregatedJobs(filters?: Record<string, string>): Promise<ApiResponse<ExtractionAggregatedDto>> {
    const params = this.buildFilters(filters);
    const queryString = new URLSearchParams(params).toString();
    return this.request<ExtractionAggregatedDto>(`/jobs/total?${queryString}`);
  }

  async clearJobs(): Promise<void> {
    await this.request('/jobs', {
      method: 'DELETE',
    });
  }
}

export const api = new ApiClient();