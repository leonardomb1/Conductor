export interface ApiResponse<T> {
  statusCode: number;
  information: string;
  error: boolean;
  entityCount?: number;
  page?: number;
  hasNestedData?: boolean;
  metadata?: FetchMetadata;
  content?: T[];
}

export interface FetchMetadata {
  extractionName?: string;
  extractionId?: number;
  requestTime?: string;
  processingTime?: string;
  dataSizeBytes?: number;
  nestedProperties?: string[];
}

export interface Destination {
  id: number;
  destinationName: string;
  destinationDbType: string;
  destinationConStr: string;
  destinationTimeZoneOffSet: number;
}

export interface Origin {
  id: number;
  originName: string;
  originAlias?: string;
  originDbType?: string;
  originConStr?: string;
  originTimeZoneOffSet?: number;
}

export interface Schedule {
  id: number;
  scheduleName: string;
  status: boolean;
  value: number;
}

export interface User {
  id: number;
  username: string;
  password?: string;
}

export interface Extraction {
  id: number;
  extractionName: string;
  scheduleId?: number;
  originId?: number;
  destinationId?: number;
  indexName?: string;
  isIncremental: boolean;
  isVirtual: boolean;
  virtualId?: string;
  virtualIdGroup?: string;
  isVirtualTemplate?: boolean;
  filterCondition?: string;
  filterColumn?: string;
  filterTime?: number;
  overrideQuery?: string;
  extractionAlias?: string;
  dependencies?: string;
  ignoreColumns?: string;
  httpMethod?: string;
  headerStructure?: string;
  endpointFullName?: string;
  bodyStructure?: string;
  offsetAttr?: string;
  offsetLimitAttr?: string;
  pageAttr?: string;
  paginationType?: string;
  totalPageAttr?: string;
  sourceType?: string;
  script?: string;
  schedule?: Schedule;
  origin?: Origin;
  destination?: Destination;
}

export interface JobDto {
  name: string;
  jobGuid: string;
  jobType: string;
  status: string;
  startTime: string;
  endTime?: string;
  timeSpentMs: number;
  megaBytes: number;
}

export interface ExtractionAggregatedDto {
  extractionId: number;
  extractionName: string;
  totalJobs: number;
  totalSizeMB: number;
  lastEndTime?: string;
  completedJobs: number;
  failedJobs: number;
  runningJobs: number;
}

// New backend-specific types
export interface SimpleExtractionDto {
  id: number;
  name: string;
}

export interface SearchSuggestion {
  type: 'extraction' | 'origin' | 'destination' | 'schedule';
  value: string;
  label: string;
  count: number;
}

export interface ExtractionAggregations {
  totalCount: number;
  withoutDestination: number;
  bySourceType: Array<{ category: string; count: number }>;
  byOrigin: Array<{ category: string; count: number }>;
  byDestination: Array<{ category: string; count: number }>;
  bySchedule: Array<{ category: string; count: number }>;
}

export interface AuthStore {
  isAuthenticated: boolean;
  token: string | null;
  user: string | null;
}

// Filter types for better type safety
export interface ExtractionFilters {
  // Basic filters
  name?: string;
  contains?: string;
  search?: string;
  
  // Entity filters
  origin?: string;
  destination?: string;
  schedule?: string;
  
  // ID filters
  originId?: string;
  destinationId?: string;
  scheduleId?: string;
  
  // Type filters
  sourceType?: string;
  isIncremental?: string;
  isVirtual?: string;
  
  // Pagination
  skip?: string;
  take?: string;
  
  // Sorting
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

export interface JobFilters {
  // Time filters
  relativeStart?: string;
  
  // Content filters
  extractionName?: string;
  status?: string;
  type?: string;
  
  // Pagination
  skip?: string;
  take?: string;
  
  // Sorting
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

export interface FetchFilters {
  // Basic filters
  name?: string;
  page?: string;
  
  // Nesting configuration
  disableNesting?: string;
  nestProperties?: string;
  nestPatterns?: string;
  excludeProperties?: string;
}