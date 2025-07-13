import type { ApiResponse, Destination, Origin, Schedule, User, Extraction, JobDto, ExtractionAggregatedDto } from './types.js';
import { auth } from './auth.svelte.js';

// Add new types for backend-specific endpoints
export interface SimpleExtractionDto {
  id: number;
  name: string;
}

export interface ExtractionCountResponse {
  count: number;
}

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

    console.log(`API Request: ${options.method || 'GET'} ${endpoint}`, {
      headers: Object.fromEntries(Object.entries(headers).filter(([key]) => key !== 'Authorization')),
      hasAuth: !!auth.token
    });

    try {
      const response = await fetch(`/api${endpoint}`, {
        ...options,
        headers,
      });

      console.log(`API Response: ${response.status} ${response.statusText}`, {
        url: `/api${endpoint}`,
        ok: response.ok
      });

      // Handle 401 Unauthorized
      if (response.status === 401) {
        console.log('Unauthorized response, clearing auth');
        auth.handleUnauthorized();
        throw new Error('Session expired - please login again');
      }

      if (!response.ok) {
        let errorMessage = `API request failed: ${response.status} ${response.statusText}`;
        try {
          const errorText = await response.text();
          console.log('Error response body:', errorText);
          if (errorText) {
            errorMessage += ` - ${errorText}`;
          }
        } catch (e) {
          console.log('Could not read error response body');
        }
        throw new Error(errorMessage);
      }

      const data = await response.json();
      console.log(`API Success:`, {
        endpoint,
        contentLength: data.content?.length,
        entityCount: data.entityCount,
        error: data.error
      });

      return data;
    } catch (error) {
      console.error(`API Error for ${endpoint}:`, error);
      
      if (error instanceof TypeError && error.message.includes('fetch')) {
        throw new Error('Network error - please check your connection');
      }
      throw error;
    }
  }

  // Login methods
  async login(username: string, password: string): Promise<string> {
    console.log('Attempting login for user:', username);
    const response = await fetch('/api/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });

    if (!response.ok) {
      console.log('Login failed:', response.status, response.statusText);
      throw new Error('Invalid username or password');
    }

    const token = await response.text();
    console.log('Login successful, token received');
    return token;
  }

  async loginLdap(username: string, password: string): Promise<string> {
    console.log('Attempting LDAP login for user:', username);
    const response = await fetch('/api/ssologin', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });

    if (!response.ok) {
      console.log('LDAP login failed:', response.status, response.statusText);
      throw new Error('LDAP authentication failed');
    }

    const token = await response.text();
    console.log('LDAP login successful, token received');
    return token;
  }

  // Health and metrics
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
    const params: Record<string, string> = {};

    // Only add take/skip if not already specified
    if (filters?.take === undefined) {
      params.take = defaultTake.toString();
    }

    if (filters?.skip === undefined) {
      params.skip = '0';
    }

    // Add all provided filters
    if (filters) {
      Object.entries(filters).forEach(([key, value]) => {
        if (value !== undefined && value !== '') {
          params[key] = value;
        }
      });
    }

    return params;
  }

  // ENHANCED EXTRACTION METHODS - utilizing all backend capabilities
  
  /**
   * Get extractions with full filtering support matching backend capabilities
   */
  async getExtractions(filters?: Record<string, string>): Promise<ApiResponse<Extraction>> {
    console.log('Getting extractions with filters:', filters);
    
    try {
      const params = this.buildFilters(filters, 20);
      const queryString = new URLSearchParams(params).toString();
      
      console.log('Final extraction query string:', queryString);
      
      const result = await this.request<Extraction>(`/extractions?${queryString}`);
      
      console.log('Extractions API result:', {
        contentCount: result.content?.length || 0,
        entityCount: result.entityCount,
        hasError: result.error,
        statusCode: result.statusCode
      });
      
      return result;
    } catch (error) {
      console.error('getExtractions failed:', error);
      throw error;
    }
  }

  /**
   * Get count of extractions matching filter criteria
   */
  async getExtractionsCount(filters?: Record<string, string>): Promise<ApiResponse<number>> {
    console.log('Getting extractions count with filters:', filters);
    
    try {
      const params: Record<string, string> = {};
      
      // Add filters excluding pagination and sorting params
      if (filters) {
        Object.entries(filters).forEach(([key, value]) => {
          if (!['skip', 'take', 'sortBy', 'sortDirection'].includes(key) && value !== '') {
            params[key] = value;
          }
        });
      }
      
      const queryString = new URLSearchParams(params).toString();
      const result = await this.request<number>(`/extractions/count?${queryString}`);
      
      console.log('Extractions count result:', result);
      return result;
    } catch (error) {
      console.error('getExtractionsCount failed:', error);
      throw error;
    }
  }

  /**
   * Get simple extraction names/IDs
   */
  async getExtractionNames(ids?: number[]): Promise<ApiResponse<SimpleExtractionDto>> {
    try {
      const params: Record<string, string> = {};
      if (ids && ids.length > 0) {
        params.ids = ids.join(',');
      }
      
      const queryString = new URLSearchParams(params).toString();
      const endpoint = queryString ? `/extractions/names?${queryString}` : '/extractions/names';
      
      return this.request<SimpleExtractionDto>(endpoint);
    } catch (error) {
      console.error('getExtractionNames failed:', error);
      throw error;
    }
  }

  /**
   * Get extraction dependencies
   */
  async getExtractionDependencies(id: number): Promise<ApiResponse<Extraction>> {
    return this.request<Extraction>(`/extractions/${id}/dependencies`);
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

  /**
   * Execute transfer with full filtering support including ID-based filtering
   */
  async executeTransfer(filters?: Record<string, string>): Promise<ApiResponse<never>> {
    console.log('Executing transfer with filters:', filters);
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<never>(`/extractions/transfer${params}`, {
      method: 'POST',
    });
  }

  /**
   * Execute pull with full filtering support including ID-based filtering
   */
  async executePull(filters?: Record<string, string>): Promise<ApiResponse<never>> {
    console.log('Executing pull with filters:', filters);
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<never>(`/extractions/pull${params}`, {
      method: 'POST',
    });
  }

  /**
   * Fetch data with pagination support
   */
  async fetchData(filters?: Record<string, string>): Promise<ApiResponse<Record<string, any>>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<Record<string, any>>(`/extractions/fetch${params}`);
  }

  // DESTINATIONS
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

  // ORIGINS
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

  // SCHEDULES
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

  // USERS
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

  // JOBS
  async getActiveJobs(): Promise<ApiResponse<JobDto>> {
    return this.request<JobDto>('/jobs/active');
  }

  // JOBS - Enhanced with proper pagination support
  async searchJobs(filters?: Record<string, string>): Promise<ApiResponse<JobDto>> {
    console.log('Searching jobs with filters:', filters);
    
    try {
      const params = this.buildFilters(filters, 20);
      const queryString = new URLSearchParams(params).toString();
      
      console.log('Jobs search query string:', queryString);
      
      const result = await this.request<JobDto>(`/jobs/search?${queryString}`);
      
      console.log('Jobs search result:', {
        contentCount: result.content?.length || 0,
        entityCount: result.entityCount,
        hasError: result.error,
        statusCode: result.statusCode
      });
      
      return result;
    } catch (error) {
      console.error('searchJobs failed:', error);
      throw error;
    }
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