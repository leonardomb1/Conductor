export interface ApiResponse<T> {
  statusCode: number
  information: string
  error: boolean
  entityCount?: number
  page?: number
  content?: T[]
}

export interface PaginatedResponse<T> {
  content: T[]
  totalElements: number
  totalPages: number
  size: number
  number: number
  first: boolean
  last: boolean
  empty: boolean
}

export interface Origin {
  id: number
  originName: string
  originAlias?: string
  originDbType?: string
  originConStr?: string
  originTimeZoneOffSet?: number
}

export interface Destination {
  id: number
  destinationName: string
  destinationDbType: string
  destinationConStr: string
  destinationTimeZoneOffSet: number
}

export interface Schedule {
  id: number
  scheduleName: string
  status: boolean
  value: number
}

export interface User {
  id: number
  username: string
  password?: string
}

export interface Extraction {
  id: number
  extractionName: string
  scheduleId?: number
  originId: number
  destinationId?: number
  indexName?: string
  isIncremental: boolean
  isVirtual: boolean
  schedule?: Schedule
  destination?: Destination
  origin?: Origin
  virtualIdColumn?: string
  virtualIdGroup?: string
  virtualTemplate?: boolean
  dependencies?: string
  filterColumn?: string
  filterTime?: number
  overrideQuery?: string
  filterCondition?: string
  sourceType?: string
  httpMethod?: string
  headerStructure?: string
  endpointFullName?: string
  bodyStructure?: string
  offsetAttr?: string
  offsetLimitAttr?: string
  pageAttr?: string
  paginationType?: string
  totalPageAttr?: string
  extractionAlias?: string
  ignoreColumns?: string
  script?: string
  isScriptBased?: boolean
}

// Updated JobDto to match backend changes
export interface JobDto {
  name: string
  jobGuid: string
  jobType: string
  status: string
  startTime: string
  endTime?: string
  timeSpentMs: number
  megaBytes: number // Changed from bytes to megaBytes
}

// New aggregated extraction data
export interface ExtractionAggregatedDto {
  extractionId: number
  extractionName: string
  totalJobs: number
  totalSizeMB: number
  lastEndTime?: string
  completedJobs: number
  failedJobs: number
  runningJobs: number
}

export interface Metric {
  name: string
  description: string
  unit: string
  type: "counter" | "gauge" | "histogram"
  value: number
  timestamp: string
  labels?: Record<string, string>
}

export interface MetricsResponse {
  statusCode: number
  information: string
  error: boolean
  content?: {
    metrics: Metric[]
    system: {
      uptime: number
      memory: {
        used: number
        total: number
        percentage: number
      }
      cpu: {
        usage: number
        cores: number
      }
      disk: {
        used: number
        total: number
        percentage: number
      }
    }
    application: {
      requests_total: number
      requests_per_second: number
      active_connections: number
      error_rate: number
      response_time_avg: number
    }
  }
}

export interface FetchResult {
  data: any[]
  totalCount: number
  page: number
  pageSize: number
  hasNextPage: boolean
  hasPreviousPage: boolean
}
