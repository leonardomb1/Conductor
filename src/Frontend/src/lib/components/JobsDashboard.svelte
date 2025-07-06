<script lang="ts">
  import { onMount } from 'svelte';
  import { Card, CardContent, CardHeader, CardTitle } from '$lib/components/ui/Card.svelte';
  import { Badge } from '$lib/components/ui/Badge.svelte';
  import { Progress } from '$lib/components/ui/Progress.svelte';
  import { Alert, AlertDescription } from '$lib/components/ui/Alert.svelte';
  import { Tabs, TabsContent, TabsList, TabsTrigger } from '$lib/components/ui/Tabs.svelte';
  import { Button } from '$lib/components/ui/Button.svelte';
  import { Input } from '$lib/components/ui/Input.svelte';
  import { Label } from '$lib/components/ui/Label.svelte';
  import { AuthService } from '$lib/auth';
  import type { JobDto, ExtractionAggregatedDto } from '$lib/types';

  interface JobAnalytics {
    totalJobs: number;
    completedJobs: number;
    failedJobs: number;
    runningJobs: number;
    totalMegaBytes: number;
    totalTimeMs: number;
    averageJobTime: number;
    averageJobSize: number;
    jobsByType: Record<string, number>;
    jobsByStatus: Record<string, number>;
    hourlyStats: Array<{
      hour: string;
      jobs: number;
      megaBytes: number;
    }>;
    topJobsBySize: JobDto[];
    topJobsByDuration: JobDto[];
  }

  let jobs: JobDto[] = [];
  let extractionTotals: ExtractionAggregatedDto[] = [];
  let activeJobs = '';
  let analytics: JobAnalytics | null = null;
  let loading = true;
  let error = '';
  let filters = {
    take: 100,
    mbs: '',
    relativeStart: '',
    relativeEnd: '',
  };

  const fetchJobs = async () => {
    try {
      // Fetch active jobs
      const activeResponse = await AuthService.authenticatedFetch('/api/jobs/active');
      const activeData = await activeResponse.json();
      activeJobs = activeData.information || 'No active jobs';

      // Build query parameters
      const params = new URLSearchParams();
      if (filters.take) params.append('take', filters.take.toString());
      if (filters.mbs) params.append('mbs', filters.mbs);
      if (filters.relativeStart) params.append('relativeStart', filters.relativeStart);
      if (filters.relativeEnd) params.append('relativeEnd', filters.relativeEnd);

      // Fetch job history
      const jobsResponse = await AuthService.authenticatedFetch(`/api/jobs/search?${params.toString()}`);
      if (!jobsResponse.ok) throw new Error('Failed to fetch jobs');

      const jobsData = await jobsResponse.json();
      const jobsList = jobsData.content || [];
      jobs = jobsList;

      // Fetch extraction totals
      const totalsResponse = await AuthService.authenticatedFetch('/api/jobs/total');
      if (totalsResponse.ok) {
        const totalsData = await totalsResponse.json();
        extractionTotals = totalsData.content || [];
      }

      // Calculate analytics
      analytics = calculateAnalytics(jobsList);
      error = '';
    } catch (err) {
      if (err instanceof Error && err.message !== 'Authentication expired') {
        error = 'Failed to load jobs data';
      }
    } finally {
      loading = false;
    }
  };

  const calculateAnalytics = (jobs: JobDto[]): JobAnalytics => {
    const totalJobs = jobs.length;
    const completedJobs = jobs.filter((j) => j.status === 'Completed').length;
    const failedJobs = jobs.filter((j) => j.status === 'Failed').length;
    const runningJobs = jobs.filter((j) => j.status === 'Running').length;

    const totalMegaBytes = jobs.reduce((sum, job) => sum + job.megaBytes, 0);
    const totalTimeMs = jobs.reduce((sum, job) => sum + job.timeSpentMs, 0);

    const averageJobTime = totalJobs > 0 ? totalTimeMs / totalJobs : 0;
    const averageJobSize = totalJobs > 0 ? totalMegaBytes / totalJobs : 0;

    // Group by job type
    const jobsByType: Record<string, number> = {};
    jobs.forEach((job) => {
      jobsByType[job.jobType] = (jobsByType[job.jobType] || 0) + 1;
    });

    // Group by status
    const jobsByStatus: Record<string, number> = {};
    jobs.forEach((job) => {
      jobsByStatus[job.status] = (jobsByStatus[job.status] || 0) + 1;
    });

    // Hourly statistics (last 24 hours)
    const hourlyStats: Array<{ hour: string; jobs: number; megaBytes: number }> = [];
    const now = new Date();
    for (let i = 23; i >= 0; i--) {
      const hour = new Date(now.getTime() - i * 60 * 60 * 1000);
      const hourStr = hour.getHours().toString().padStart(2, '0') + ':00';
      const hourJobs = jobs.filter((job) => {
        const jobHour = new Date(job.startTime).getHours();
        return jobHour === hour.getHours();
      });

      hourlyStats.push({
        hour: hourStr,
        jobs: hourJobs.length,
        megaBytes: hourJobs.reduce((sum, job) => sum + job.megaBytes, 0),
      });
    }

    // Top jobs by size and duration
    const topJobsBySize = [...jobs].sort((a, b) => b.megaBytes - a.megaBytes).slice(0, 10);
    const topJobsByDuration = [...jobs].sort((a, b) => b.timeSpentMs - a.timeSpentMs).slice(0, 10);

    return {
      totalJobs,
      completedJobs,
      failedJobs,
      runningJobs,
      totalMegaBytes,
      totalTimeMs,
      averageJobTime,
      averageJobSize,
      jobsByType,
      jobsByStatus,
      hourlyStats,
      topJobsBySize,
      topJobsByDuration,
    };
  };

  onMount(() => {
    fetchJobs();
  });

  const formatBytes = (megaBytes: number) => {
    if (megaBytes === 0) return '0 MB';
    if (megaBytes < 1) return `${(megaBytes * 1024).toFixed(2)} KB`;
    if (megaBytes < 1024) return `${megaBytes.toFixed(2)} MB`;
    return `${(megaBytes / 1024).toFixed(2)} GB`;
  };

  const formatDuration = (ms: number) => {
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
    if (ms < 3600000) return `${(ms / 60000).toFixed(1)}m`;
    return `${(ms / 3600000).toFixed(1)}h`;
  };

  const getStatusColor = (status: string) => {
    switch (status.toLowerCase()) {
      case 'completed':
        return 'default';
      case 'running':
        return 'secondary';
      case 'failed':
        return 'destructive';
      default:
        return 'outline';
    }
  };
</script>

<div class="space-y-6">
  <div class="flex items-center justify-between">
    <h2 class="text-2xl font-bold">Jobs Analytics</h2>
    <Button onclick={fetchJobs} variant="outline">
      <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
      </svg>
      Refresh
    </Button>
  </div>

  {#if error}
    <Alert variant="destructive">
      <AlertDescription>{error}</AlertDescription>
    </Alert>
  {/if}

  {#if loading}
    <div class="flex items-center justify-center py-8">Loading jobs analytics...</div>
  {:else}
    <Tabs defaultValue="overview" class="w-full">
      <TabsList>
        <TabsTrigger value="overview">Overview</TabsTrigger>
        <TabsTrigger value="extractions">By Extraction</TabsTrigger>
        <TabsTrigger value="breakdown">Breakdown</TabsTrigger>
        <TabsTrigger value="top-jobs">Top Jobs</TabsTrigger>
        <TabsTrigger value="filters">Filters</TabsTrigger>
      </TabsList>

      <TabsContent value="overview" class="space-y-6">
        {#if analytics}
          <div class="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            <Card>
              <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
                <CardTitle class="text-sm font-medium">Total Jobs</CardTitle>
                <svg class="h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
                </svg>
              </CardHeader>
              <CardContent>
                <div class="text-2xl font-bold">{analytics.totalJobs.toLocaleString()}</div>
                <p class="text-xs text-muted-foreground">{analytics.runningJobs} currently running</p>
              </CardContent>
            </Card>

            <Card>
              <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
                <CardTitle class="text-sm font-medium">Success Rate</CardTitle>
                <svg class="h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" />
                </svg>
              </CardHeader>
              <CardContent>
                {#if analytics.totalJobs > 0}
                  {#let successRate = (analytics.completedJobs / analytics.totalJobs) * 100}
                    <div class="text-2xl font-bold">{successRate.toFixed(1)}%</div>
                    <Progress value={successRate} class="mt-2" />
                    <p class="text-xs text-muted-foreground mt-1">
                      {analytics.completedJobs} completed, {analytics.failedJobs} failed
                    </p>
                  {/let}
                {:else}
                  <div class="text-2xl font-bold">0%</div>
                  <Progress value={0} class="mt-2" />
                  <p class="text-xs text-muted-foreground mt-1">
                    0 completed, 0 failed
                  </p>
                {/if}
              </CardContent>
            </Card>

            <Card>
              <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
                <CardTitle class="text-sm font-medium">Total Data Processed</CardTitle>
                <svg class="h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4" />
                </svg>
              </CardHeader>
              <CardContent>
                <div class="text-2xl font-bold">{formatBytes(analytics.totalMegaBytes)}</div>
                <p class="text-xs text-muted-foreground">Avg: {formatBytes(analytics.averageJobSize)} per job</p>
              </CardContent>
            </Card>

            <Card>
              <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
                <CardTitle class="text-sm font-medium">Avg Job Duration</CardTitle>
                <svg class="h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </CardHeader>
              <CardContent>
                <div class="text-2xl font-bold">{formatDuration(analytics.averageJobTime)}</div>
                <p class="text-xs text-muted-foreground">Total: {formatDuration(analytics.totalTimeMs)}</p>
              </CardContent>
            </Card>
          </div>
        {/if}

        <div class="grid gap-4 md:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>Active Jobs</CardTitle>
            </CardHeader>
            <CardContent>
              <p class="text-sm text-muted-foreground">{activeJobs}</p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Recent Activity</CardTitle>
            </CardHeader>
            <CardContent>
              <div class="space-y-2">
                {#each jobs.slice(0, 5) as job}
                  <div class="flex items-center justify-between text-sm">
                    <span>{job.name}</span>
                    <Badge variant={getStatusColor(job.status)}>{job.status}</Badge>
                  </div>
                {/each}
              </div>
            </CardContent>
          </Card>
        </div>
      </TabsContent>

      <TabsContent value="extractions" class="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle>Data by Extraction</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="space-y-4">
              {#each extractionTotals as extraction}
                <div class="border rounded-lg p-4">
                  <div class="flex items-center justify-between mb-2">
                    <h3 class="font-medium">{extraction.extractionName}</h3>
                    <Badge variant="outline">ID: {extraction.extractionId}</Badge>
                  </div>
                  
                  <div class="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                    <div>
                      <span class="text-muted-foreground">Total Jobs:</span>
                      <div class="font-bold">{extraction.totalJobs}</div>
                    </div>
                    <div>
                      <span class="text-muted-foreground">Total Size:</span>
                      <div class="font-bold">{formatBytes(extraction.totalSizeMB)}</div>
                    </div>
                    <div>
                      <span class="text-muted-foreground">Completed:</span>
                      <div class="font-bold text-green-600">{extraction.completedJobs}</div>
                    </div>
                    <div>
                      <span class="text-muted-foreground">Failed:</span>
                      <div class="font-bold text-red-600">{extraction.failedJobs}</div>
                    </div>
                  </div>
                  
                  {#if extraction.lastEndTime}
                    <div class="mt-2 text-xs text-muted-foreground">
                      Last completed: {new Date(extraction.lastEndTime).toLocaleString()}
                    </div>
                  {/if}
                  
                  {#if extraction.totalJobs > 0}
                    {#let successRate = (extraction.completedJobs / extraction.totalJobs) * 100}
                      <Progress value={successRate} class="mt-2" />
                    {/let}
                  {:else}
                    <Progress value={0} class="mt-2" />
                  {/if}
                </div>
              {/each}
            </div>
          </CardContent>
        </Card>
      </TabsContent>

      <TabsContent value="breakdown" class="space-y-6">
        {#if analytics}
          <div class="grid gap-4 md:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle>Jobs by Type</CardTitle>
              </CardHeader>
              <CardContent>
                <div class="space-y-2">
                  {#each Object.entries(analytics.jobsByType) as [type, count]}
                    <div class="flex items-center justify-between">
                      <span class="font-medium">{type}</span>
                      <div class="flex items-center gap-2">
                        <Badge variant="outline">{count}</Badge>
                        <div class="w-20">
                          <Progress value={(count / analytics.totalJobs) * 100} />
                        </div>
                      </div>
                    </div>
                  {/each}
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Jobs by Status</CardTitle>
              </CardHeader>
              <CardContent>
                <div class="space-y-2">
                  {#each Object.entries(analytics.jobsByStatus) as [status, count]}
                    <div class="flex items-center justify-between">
                      <span class="font-medium">{status}</span>
                      <div class="flex items-center gap-2">
                        <Badge variant={getStatusColor(status)}>{count}</Badge>
                        <div class="w-20">
                          <Progress value={(count / analytics.totalJobs) * 100} />
                        </div>
                      </div>
                    </div>
                  {/each}
                </div>
              </CardContent>
            </Card>
          </div>
        {/if}
      </TabsContent>

      <TabsContent value="top-jobs" class="space-y-6">
        {#if analytics}
          <div class="grid gap-4 md:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle>Top Jobs by Data Size</CardTitle>
              </CardHeader>
              <CardContent>
                <div class="space-y-2">
                  {#each analytics.topJobsBySize as job}
                    <div class="flex items-center justify-between p-2 border rounded">
                      <div class="flex-1">
                        <div class="font-medium">{job.name}</div>
                        <div class="text-sm text-muted-foreground">{new Date(job.startTime).toLocaleString()}</div>
                      </div>
                      <div class="flex items-center gap-2">
                        <Badge variant={getStatusColor(job.status)}>{job.status}</Badge>
                        <div class="text-right">
                          <div class="font-bold">{formatBytes(job.megaBytes)}</div>
                          <div class="text-xs text-muted-foreground">{formatDuration(job.timeSpentMs)}</div>
                        </div>
                      </div>
                    </div>
                  {/each}
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Top Jobs by Duration</CardTitle>
              </CardHeader>
              <CardContent>
                <div class="space-y-2">
                  {#each analytics.topJobsByDuration as job}
                    <div class="flex items-center justify-between p-2 border rounded">
                      <div class="flex-1">
                        <div class="font-medium">{job.name}</div>
                        <div class="text-sm text-muted-foreground">{new Date(job.startTime).toLocaleString()}</div>
                      </div>
                      <div class="flex items-center gap-2">
                        <Badge variant={getStatusColor(job.status)}>{job.status}</Badge>
                        <div class="text-right">
                          <div class="font-bold">{formatDuration(job.timeSpentMs)}</div>
                          <div class="text-xs text-muted-foreground">{formatBytes(job.megaBytes)}</div>
                        </div>
                      </div>
                    </div>
                  {/each}
                </div>
              </CardContent>
            </Card>
          </div>
        {/if}
      </TabsContent>

      <TabsContent value="filters">
        <Card>
          <CardHeader>
            <CardTitle>Filters</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="grid grid-cols-2 md:grid-cols-4 gap-4">
              <div>
                <Label for="take">Max Results</Label>
                <Input
                  id="take"
                  type="number"
                  bind:value={filters.take}
                />
              </div>
              <div>
                <Label for="mbs">Min Size (MB)</Label>
                <Input
                  id="mbs"
                  type="number"
                  step="0.1"
                  bind:value={filters.mbs}
                />
              </div>
              <div>
                <Label for="relativeStart">Hours Ago (Start)</Label>
                <Input
                  id="relativeStart"
                  type="number"
                  bind:value={filters.relativeStart}
                />
              </div>
              <div>
                <Label for="relativeEnd">Hours Ago (End)</Label>
                <Input
                  id="relativeEnd"
                  type="number"
                  bind:value={filters.relativeEnd}
                />
              </div>
            </div>
            <Button onclick={fetchJobs} class="mt-4">
              <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
              Apply Filters
            </Button>
          </CardContent>
        </Card>
      </TabsContent>
    </Tabs>
  {/if}
</div>
