export interface ApiResponse<T> {
  statusCode: number;
  information: string;
  error: boolean;
  entityCount?: number;
  page?: number;
  content?: T[];
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

export interface AuthStore {
  isAuthenticated: boolean;
  token: string | null;
  user: string | null;
}
