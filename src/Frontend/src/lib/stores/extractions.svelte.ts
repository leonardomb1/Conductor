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
    // Don't reload if we have fresh data and not forcing
    if (!force && !this.isStale && this.state.data.length > 0) {
      return true;
    }

    this.state.loading = true;
    this.state.error = null;

    try {
      // Load larger dataset for better client-side filtering
      const response = await api.getExtractions({ 
        take: "2000",
        skip: "0"
      });
      
      this.state.data = response.content || [];
      this.state.lastLoaded = new Date();
      this.state.error = null;
      
      return true;
    } catch (error) {
      console.error('Failed to load extractions:', error);
      this.state.error = error instanceof Error ? error.message : 'Failed to load extractions';
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
}

export const extractionsStore = new ExtractionsStore();