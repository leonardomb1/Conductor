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

    if (auth.token) {
      headers.Authorization = `Bearer ${auth.token}`;
    } 

    try {
      const response = await fetch(`/api${endpoint}`, {
        ...options,
        headers,
      });

      if (response.status === 401) {
        auth.handleUnauthorized();
        throw new Error('Session expired - please login again');
      }

      if (!response.ok) {
        let errorMessage = `API request failed: ${response.status} ${response.statusText}`;
        const errorText = await response.text();
        if (errorText) {
          errorMessage += ` - ${errorText}`;
        }
        throw new Error(errorMessage);
      }

      if (response.status === 204) {
        return {
          statusCode: 204,
          information: 'Operation completed successfully',
          error: false,
          content: []
        } as ApiResponse<T>;
      }

      return await response.json();
    } catch (error) {
      if (error instanceof TypeError && error.message.includes('fetch')) {
        throw new Error('Network error - please check your connection');
      }
      throw error;
    }
  }

  async login(username: string, password: string): Promise<string> {
    const response = await fetch('/api/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });

    if (!response.ok) {
      throw new Error('Invalid username or password');
    }

    return await response.text();
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

    return await response.text();
  }

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

  private buildFilters(filters?: Record<string, string>, defaultTake = 20): Record<string, string> {
    const params: Record<string, string> = {};

    if (filters?.take === undefined) {
      params.take = defaultTake.toString();
    }

    if (filters?.skip === undefined) {
      params.skip = '0';
    }

    if (filters) {
      Object.entries(filters).forEach(([key, value]) => {
        if (value !== undefined && value !== '') {
          params[key] = value;
        }
      });
    }

    return params;
  }

  async getExtractions(filters?: Record<string, string>): Promise<ApiResponse<Extraction>> {
    const params = this.buildFilters(filters, 20);
    const queryString = new URLSearchParams(params).toString();
      
    return await this.request<Extraction>(`/extractions?${queryString}`);
  }

  async getExtractionsCount(filters?: Record<string, string>): Promise<ApiResponse<number>> {
    const params: Record<string, string> = {};
      
    if (filters) {
      Object.entries(filters).forEach(([key, value]) => {
        if (!['skip', 'take', 'sortBy', 'sortDirection'].includes(key) && value !== '') {
          params[key] = value;
        }
      });
    }
      
    const queryString = new URLSearchParams(params).toString();
    return await this.request<number>(`/extractions/count?${queryString}`);
  }

  async getExtractionNames(ids?: number[]): Promise<ApiResponse<SimpleExtractionDto>> {
    const params: Record<string, string> = {};
    if (ids && ids.length > 0) {
      params.ids = ids.join(',');
    }
    
    const queryString = new URLSearchParams(params).toString();
    const endpoint = queryString ? `/extractions/names?${queryString}` : '/extractions/names';
    
    return this.request<SimpleExtractionDto>(endpoint);
  }

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

  async updateExtraction(id: number, extraction: Omit<Extraction, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>(`/extractions/${id}`, {
      method: 'PUT',
      body: JSON.stringify(extraction),
    });
  }

  async deleteExtraction(id: number): Promise<ApiResponse<never>> {
    try {
      return await this.request<never>(`/extractions/${id}`, {
        method: 'DELETE',
      });
    } catch (error) {
      
      if (error instanceof Error) {
        if (error.message.includes('404')) {
          throw new Error('Extraction not found - it may have already been deleted');
        } else if (error.message.includes('403')) {
          throw new Error('You do not have permission to delete this extraction');
        } else if (error.message.includes('409')) {
          throw new Error('Cannot delete extraction - it may be in use by running jobs or have dependencies');
        }
      }
      
      throw error;
    }
  }

  async executeTransfer(filters?: Record<string, string>): Promise<ApiResponse<never>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<never>(`/extractions/programTransfer${params}`, {
      method: 'PUT',
    });
  }

  async executePull(filters?: Record<string, string>): Promise<ApiResponse<never>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<never>(`/extractions/programPull${params}`, {
      method: 'PUT',
    });
  }

  async fetchData(filters?: Record<string, string>): Promise<ApiResponse<Record<string, any>>> {
    const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
    return this.request<Record<string, any>>(`/extractions/fetch${params}`);
  }

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

  async updateDestination(id: number, destination: Omit<Destination, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>(`/destinations/${id}`, {
      method: 'PUT',
      body: JSON.stringify(destination),
    });
  }

  async deleteDestination(id: number): Promise<ApiResponse<never>> {
    try {     
      return await this.request<never>(`/destinations/${id}`, {
        method: 'DELETE',
      });
    } catch (error) {
      if (error instanceof Error) {
        if (error.message.includes('404')) {
          throw new Error('Destination not found - it may have already been deleted');
        } else if (error.message.includes('409')) {
          throw new Error('Cannot delete destination - it is being used by one or more extractions');
        }
      }
      
      throw error;
    }
  }

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

  async updateOrigin(id: number, origin: Omit<Origin, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>(`/origins/${id}`, {
      method: 'PUT',
      body: JSON.stringify(origin),
    });
  }

  async deleteOrigin(id: number): Promise<ApiResponse<never>> {
    try {
      return await this.request<never>(`/origins/${id}`, {
        method: 'DELETE',
      });
    } catch (error) {
      
      if (error instanceof Error) {
        if (error.message.includes('404')) {
          throw new Error('Origin not found - it may have already been deleted');
        } else if (error.message.includes('409')) {
          throw new Error('Cannot delete origin - it is being used by one or more extractions');
        }
      }
      
      throw error;
    }
  }

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

  async updateSchedule(id: number, schedule: Omit<Schedule, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>(`/schedules/${id}`, {
      method: 'PUT',
      body: JSON.stringify(schedule),
    });
  }

  async deleteSchedule(id: number): Promise<ApiResponse<never>> {
    try {
      return await this.request<never>(`/schedules/${id}`, {
        method: 'DELETE',
      });
    } catch (error) {
      if (error instanceof Error) {
        if (error.message.includes('404')) {
          throw new Error('Schedule not found - it may have already been deleted');
        } else if (error.message.includes('409')) {
          throw new Error('Cannot delete schedule - it is being used by one or more extractions');
        }
      }
      
      throw error;
    }
  }

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

  async updateUser(id: number, user: Omit<User, 'id'>): Promise<ApiResponse<never>> {
    return this.request<never>(`/users/${id}`, {
      method: 'PUT',
      body: JSON.stringify(user),
    });
  }

  async deleteUser(id: number): Promise<ApiResponse<never>> {
    try {
      return await this.request<never>(`/users/${id}`, {
        method: 'DELETE',
      });
    } catch (error) {
      if (error instanceof Error) {
        if (error.message.includes('404')) {
          throw new Error('User not found - it may have already been deleted');
        } else if (error.message.includes('403')) {
          throw new Error('You do not have permission to delete this user');
        }
      }
      
      throw error;
    }
  }

  async getActiveJobs(): Promise<ApiResponse<JobDto>> {
    return this.request<JobDto>('/jobs/active');
  }

  async searchJobs(filters?: Record<string, string>): Promise<ApiResponse<JobDto>> { 
    const params = this.buildFilters(filters, 20);
    const queryString = new URLSearchParams(params).toString();

    return await this.request<JobDto>(`/jobs/search?${queryString}`);
  }

  async getAggregatedJobs(filters?: Record<string, string>): Promise<ApiResponse<ExtractionAggregatedDto>> {
    const params = this.buildFilters(filters);
    const queryString = new URLSearchParams(params).toString();
    return this.request<ExtractionAggregatedDto>(`/jobs/total?${queryString}`);
  }

  async clearJobs(): Promise<ApiResponse<never>> {
    return this.request<never>('/jobs', {
      method: 'DELETE',
    });
  }

  async getRecentExtractions(limit = 5): Promise<ApiResponse<Extraction>> {
    try {
      return this.request<Extraction>(`/extractions/recent?take=${limit}`);
    } catch (error) {
      return this.getExtractions({ 
        take: limit.toString(), 
        sortBy: 'id', 
        sortDirection: 'desc' 
      });
    }
  }

  async getPopularExtractions(limit = 5): Promise<ApiResponse<Extraction>> {
    try {
      return this.request<Extraction>(`/extractions/popular?take=${limit}`);
    } catch (error) {
      return this.getExtractions({ take: limit.toString() });
    }
  }

  async getExtractionAggregations(filters?: Record<string, string>): Promise<ApiResponse<any>> {
    try {
      const params = filters ? '?' + new URLSearchParams(filters).toString() : '';
      return this.request<any>(`/extractions/aggregations${params}`);
    } catch (error) {
      return {
        statusCode: 200,
        information: 'Aggregations not available',
        error: false,
        content: []
      } as ApiResponse<any>;
    }
  }

  async getSearchSuggestions(query: string): Promise<ApiResponse<any>> {
    try {
      return this.request<any>(`/extractions/suggestions?q=${encodeURIComponent(query)}`);
    } catch (error) {
      return {
        statusCode: 200,
        information: 'Suggestions not available',
        error: false,
        content: []
      } as ApiResponse<any>;
    }
  }
}

export const api = new ApiClient();