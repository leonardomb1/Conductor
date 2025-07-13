import { api } from '$lib/api.ts';
import type { Extraction } from '$lib/types.ts';

interface ExtractionsState {
  data: Extraction[];
  loading: boolean;
  lastLoaded: Date | null;
  error: string | null;
}

class ExtractionsStore {
  private state = $state<ExtractionsState>({
    data: [],
    loading: false,
    lastLoaded: null,
    error: null
  });

  // Cache duration: 5 minutes
  private static readonly CACHE_DURATION = 5 * 60 * 1000;

  get data() {
    return this.state.data;
  }

  get loading() {
    return this.state.loading;
  }

  get lastLoaded() {
    return this.state.lastLoaded;
  }

  get error() {
    return this.state.error;
  }

  get isStale() {
    if (!this.state.lastLoaded) return true;
    return Date.now() - this.state.lastLoaded.getTime() > ExtractionsStore.CACHE_DURATION;
  }

  async loadExtractions(force = false): Promise<boolean> {
    console.log('ExtractionsStore.loadExtractions called', { force, isStale: this.isStale, dataLength: this.state.data.length });
    
    // Don't reload if we have fresh data and not forcing
    if (!force && !this.isStale && this.state.data.length > 0) {
      console.log('Using cached data');
      return true;
    }

    this.state.loading = true;
    this.state.error = null;

    try {
      console.log('Making API call to load extractions...');
      
      // Try different API call strategies
      let response;
      
      // Strategy 1: Try with no filters first
      try {
        console.log('Trying API call with no filters...');
        response = await api.getExtractions();
        console.log('API response received:', response);
      } catch (noFilterError) {
        console.log('No filter call failed, trying with basic pagination...');
        // Strategy 2: Try with basic pagination
        response = await api.getExtractions({ 
          take: "1000",
          skip: "0"
        });
        console.log('Pagination API response received:', response);
      }
      
      if (!response) {
        throw new Error('No response received from API');
      }
      
      if (response.error) {
        throw new Error(response.information || 'API returned error response');
      }
      
      const extractions = response.content || [];
      console.log('Extractions loaded:', extractions.length);
      
      this.state.data = extractions;
      this.state.lastLoaded = new Date();
      this.state.error = null;
      
      return true;
    } catch (error) {
      console.error('Failed to load extractions:', error);
      
      // Provide more detailed error information
      let errorMessage = 'Failed to load extractions';
      
      if (error instanceof Error) {
        errorMessage = error.message;
      } else if (typeof error === 'string') {
        errorMessage = error;
      } else if (error && typeof error === 'object') {
        errorMessage = JSON.stringify(error);
      }
      
      this.state.error = errorMessage;
      
      // Don't clear existing data on error - allow user to work with stale data
      if (this.state.data.length === 0) {
        this.state.data = [];
      }
      
      return false;
    } finally {
      this.state.loading = false;
    }
  }

  async refreshExtractions(): Promise<boolean> {
    console.log('Refreshing extractions...');
    return this.loadExtractions(true);
  }

  addExtraction(extraction: Extraction) {
    console.log('Adding extraction:', extraction.extractionName);
    this.state.data = [...this.state.data, extraction];
  }

  updateExtraction(id: number, extraction: Extraction) {
    console.log('Updating extraction:', id);
    const index = this.state.data.findIndex(e => e.id === id);
    if (index !== -1) {
      this.state.data[index] = { ...extraction, id };
    }
  }

  removeExtraction(id: number) {
    console.log('Removing extraction:', id);
    this.state.data = this.state.data.filter(e => e.id !== id);
  }

  clearCache() {
    console.log('Clearing extractions cache');
    this.state.data = [];
    this.state.lastLoaded = null;
    this.state.error = null;
  }

  // Debug method to inspect current state
  getDebugInfo() {
    return {
      dataCount: this.state.data.length,
      loading: this.state.loading,
      lastLoaded: this.state.lastLoaded,
      error: this.state.error,
      isStale: this.isStale,
      sampleData: this.state.data.slice(0, 3).map(e => ({ 
        id: e.id, 
        name: e.extractionName,
        sourceType: e.sourceType 
      }))
    };
  }
}

export const extractionsStore = new ExtractionsStore();