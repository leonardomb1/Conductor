<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api.js';
  import PageHeader from '$lib/components/layout/PageHeader.svelte';
  import Card from '$lib/components/ui/Card.svelte';
  import Badge from '$lib/components/ui/Badge.svelte';
  import { 
    Database, 
    FileText, 
    Users, 
    Settings, 
    BarChart3,
    Calendar,
    Download,
    Upload,
    Activity,
    Clock
  } from '@lucide/svelte';

  let healthData = $state<any>(null);
  let metricsData = $state<any>(null);
  let activeJobs = $state<any[]>([]);
  let recentJobs = $state<any[]>([]);
  let loading = $state(true);

  onMount(async () => {
    try {
      const [health, metrics, activeJobsRes, recentJobsRes] = await Promise.all([
        api.getHealth(),
        api.getMetrics(),
        api.getActiveJobs(),
        api.searchJobs({ relativeStart: '86400' }) // Last 24 hours
      ]);

      healthData = health;
      metricsData = metrics;
      activeJobs = activeJobsRes.content || [];
      recentJobs = recentJobsRes.content?.slice(0, 10) || [];
    } finally {
      loading = false;
    }
  });

  function formatBytes(bytes: number): string {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  }

  function formatDuration(ms: number): string {
    const seconds = Math.floor(ms / 1000);
    const minutes = Math.floor(seconds / 60);
    const hours = Math.floor(minutes / 60);
    
    if (hours > 0) return `${hours}h ${minutes % 60}m`;
    if (minutes > 0) return `${minutes}m ${seconds % 60}s`;
    return `${seconds}s`;
  }

  function getStatusBadgeVariant(status: string) {
    switch (status.toLowerCase()) {
      case 'completed': return 'success';
      case 'running': return 'info';
      case 'failed': return 'error';
      default: return 'default';
    }
  }
</script>

<svelte:head>
  <title>Dashboard - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader 
    title="Dashboard" 
    description="Monitor your ETL pipeline performance and system health"
  />

  <!-- System Status Cards -->
  <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 sm:gap-6">
    <Card class="mobile-card">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <Activity class="h-8 w-8 text-supabase-green" />
        </div>
        <div class="ml-4 min-w-0 flex-1">
          <p class="text-sm font-medium text-gray-600 dark:text-gray-400">System Status</p>
          <div class="flex items-center space-x-2">
            <div class="w-2 h-2 bg-green-500 rounded-full"></div>
            <p class="text-lg font-semibold text-gray-900 dark:text-white truncate">
              {healthData?.status || 'Loading...'}
            </p>
          </div>
        </div>
      </div>
    </Card>

    <Card class="mobile-card">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <Clock class="h-8 w-8 text-blue-500" />
        </div>
        <div class="ml-4 min-w-0 flex-1">
          <p class="text-sm font-medium text-gray-600 dark:text-gray-400">Active Jobs</p>
          <p class="text-lg font-semibold text-gray-900 dark:text-white">
            {healthData?.activeJobs || 0}
          </p>
        </div>
      </div>
    </Card>

    <Card class="mobile-card">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <Database class="h-8 w-8 text-purple-500" />
        </div>
        <div class="ml-4 min-w-0 flex-1">
          <p class="text-sm font-medium text-gray-600 dark:text-gray-400">Active Connections</p>
          <p class="text-lg font-semibold text-gray-900 dark:text-white">
            {metricsData?.connectionPools?.totalConnections || 0}
          </p>
        </div>
      </div>
    </Card>

    <Card class="mobile-card">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <BarChart3 class="h-8 w-8 text-orange-500" />
        </div>
        <div class="ml-4 min-w-0 flex-1">
          <p class="text-sm font-medium text-gray-600 dark:text-gray-400">Memory Usage</p>
          <p class="text-lg font-semibold text-gray-900 dark:text-white">
            {metricsData?.dataTables?.estimatedMemoryMB?.toFixed(1) || '0'} MB
          </p>
        </div>
      </div>
    </Card>
  </div>

  <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
    <!-- Active Jobs -->
    <Card title="Active Jobs" description="Currently running extraction jobs" class="mobile-card">
      {#if loading}
        <div class="flex justify-center py-8">
          <svg class="animate-spin h-8 w-8 text-gray-500 dark:text-gray-400" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
        </div>
      {:else if activeJobs.length === 0}
        <div class="text-center py-8 text-gray-500 dark:text-gray-400">
          No active jobs
        </div>
      {:else}
        <div class="space-y-3">
          {#each activeJobs.slice(0, 5) as job}
            <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between p-3 bg-gray-50 dark:bg-gray-700/50 rounded-md space-y-2 sm:space-y-0">
              <div class="min-w-0 flex-1">
                <p class="font-medium text-gray-900 dark:text-white truncate">{job.name}</p>
                <p class="text-sm text-gray-600 dark:text-gray-400">{job.jobType}</p>
              </div>
              <div class="flex items-center space-x-2 flex-shrink-0">
                <Badge variant={getStatusBadgeVariant(job.status)}>
                  {job.status}
                </Badge>
                <span class="text-sm text-gray-500 dark:text-gray-400">
                  {formatBytes(job.megaBytes * 1024 * 1024)}
                </span>
              </div>
            </div>
          {/each}
        </div>
      {/if}
    </Card>

    <!-- Recent Jobs -->
    <Card title="Recent Jobs" description="Latest completed extraction jobs" class="mobile-card">
      {#if loading}
        <div class="flex justify-center py-8">
          <svg class="animate-spin h-8 w-8 text-gray-500 dark:text-gray-400" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
        </div>
      {:else if recentJobs.length === 0}
        <div class="text-center py-8 text-gray-500 dark:text-gray-400">
          No recent jobs
        </div>
      {:else}
        <div class="space-y-3">
          {#each recentJobs as job}
            <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between p-3 bg-gray-50 dark:bg-gray-700/50 rounded-md space-y-2 sm:space-y-0">
              <div class="min-w-0 flex-1">
                <p class="font-medium text-gray-900 dark:text-white truncate">{job.name}</p>
                <p class="text-sm text-gray-600 dark:text-gray-400">
                  {formatDuration(job.timeSpentMs)} • {formatBytes(job.megaBytes * 1024 * 1024)}
                </p>
              </div>
              <Badge variant={getStatusBadgeVariant(job.status)} class="flex-shrink-0">
                {job.status}
              </Badge>
            </div>
          {/each}
        </div>
      {/if}
    </Card>
  </div>
</div>