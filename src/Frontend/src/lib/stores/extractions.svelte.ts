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
    if (!force && !this.isStale && this.state.data.length > 0) {
      return true;
    }

    this.state.loading = true;
    this.state.error = null;

    try {
      let response;
      
      try {
        response = await api.getExtractions();
      } catch (noFilterError) {
        response = await api.getExtractions({ 
          take: "1000",
          skip: "0"
        });
      }
      
      if (!response) {
        throw new Error('No response received from API');
      }
      
      if (response.error) {
        throw new Error(response.information || 'API returned error response');
      }
      
      const extractions = response.content || [];
      
      this.state.data = extractions;
      this.state.lastLoaded = new Date();
      this.state.error = null;
      
      return true;
    } catch (error) {
      let errorMessage = 'Failed to load extractions';
      
      if (error instanceof Error) {
        errorMessage = error.message;
      } else if (typeof error === 'string') {
        errorMessage = error;
      } else if (error && typeof error === 'object') {
        errorMessage = JSON.stringify(error);
      }
      
      this.state.error = errorMessage;
      
      if (this.state.data.length === 0) {
        this.state.data = [];
      }
      
      return false;
    } finally {
      this.state.loading = false;
    }
  }

  async refreshExtractions(): Promise<boolean> {
    return this.loadExtractions(true);
  }

  addExtraction(extraction: Extraction) {
    this.state.data = [...this.state.data, extraction];
  }

  updateExtraction(id: number, extraction: Extraction) {
    const index = this.state.data.findIndex(e => e.id === id);
    if (index !== -1) {
      this.state.data[index] = { ...extraction, id };
    }
  }

  removeExtraction(id: number) {
    this.state.data = this.state.data.filter(e => e.id !== id);
  }

  clearCache() {
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