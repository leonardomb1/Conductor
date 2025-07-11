import type { ApiResponse, Destination, Origin, Schedule, User, Extraction, JobDto, ExtractionAggregatedDto } from './types.js';

const API_BASE = import.meta.env.PUBLIC_API_BASE_URL

class ApiClient {
  private token: string | null = null;

  setToken(token: string | null) {
    this.token = token;
  }

  private async request<T>(endpoint: string, options: RequestInit = {}): Promise<ApiResponse<T>> {
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
      ...options.headers,
    };

    if (this.token) {
      headers.Authorization = `Bearer ${this.token}`;
    }

    const response = await fetch(`${API_BASE}${endpoint}`, {
      ...options,
      headers,
    });

    if (!response.ok) {
      throw new Error(`API request failed: ${response.statusText}`);
    }

    return response.json();
  }

  // Auth
  async login(username: string, password: string): Promise<string> {
    const response = await fetch(`${API_BASE}/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });

    if (!response.ok) {
      throw new Error('Login failed');
    }

    return response.text();
  }

  async loginLdap(username: string, password: string): Promise<string> {
    const response = await fetch(`${API_BASE}/ssologin`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });

    if (!response.ok) {
      throw new Error('LDAP login failed');
    }

    return response.text();
  }

  // Destinations
  async getDestinations(filters?: Record<string, string>): Promise<ApiResponse<Destination>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<Destination>(`/destinations${params}`);
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
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<Origin>(`/origins${params}`);
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
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<Schedule>(`/schedules${params}`);
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
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<User>(`/users${params}`);
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
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<Extraction>(`/extractions${params}`);
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
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<JobDto>(`/jobs/search${params}`);
  }

  async getAggregatedJobs(filters?: Record<string, string>): Promise<ApiResponse<ExtractionAggregatedDto>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<ExtractionAggregatedDto>(`/jobs/total${params}`);
  }

  async clearJobs(): Promise<void> {
    await this.request('/jobs', {
      method: 'DELETE',
    });
  }

  // Health
  async getHealth(): Promise<{ status: string; timestamp: string; activeJobs: number }> {
    const response = await fetch(`${API_BASE}/health`);
    return response.json();
  }

  async getMetrics(): Promise<any> {
    const response = await fetch(`${API_BASE}/metrics/json`);
    return response.json();
  }
}

export const api = new ApiClient();
