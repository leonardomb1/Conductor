<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api.js';
  import type { JobDto, ExtractionAggregatedDto } from '$lib/types.js';
  import PageHeader from '$lib/components/layout/PageHeader.svelte';
  import Table from '$lib/components/ui/Table.svelte';
  import Button from '$lib/components/ui/Button.svelte';
  import Input from '$lib/components/ui/Input.svelte';
  import Select from '$lib/components/ui/Select.svelte';
  import Badge from '$lib/components/ui/Badge.svelte';
  import Card from '$lib/components/ui/Card.svelte';
  import Toast from '$lib/components/ui/Toast.svelte';
  import { Trash2, RefreshCw, BarChart3, Clock, CheckCircle, XCircle } from '@lucide/svelte';

  let activeJobs = $state<JobDto[]>([]);
  let recentJobs = $state<JobDto[]>([]);
  let aggregatedJobs = $state<ExtractionAggregatedDto[]>([]);
  let loading = $state(true);
  let activeView = $state<'active' | 'recent' | 'aggregated'>('active');
  
  // Pagination for recent jobs
  let currentPage = $state(1);
  let totalPages = $state(1);
  let totalItems = $state(0);
  let pageSize = $state(20);
  
  // Pagination for aggregated jobs
  let aggregatedCurrentPage = $state(1);
  let aggregatedTotalPages = $state(1);
  let aggregatedTotalItems = $state(0);
  let aggregatedPageSize = $state(20);
  
  // Filters for recent jobs
  let searchTerm = $state('');
  let filterStatus = $state('');
  let filterType = $state('');
  let relativeTime = $state('86400'); // Last 24 hours
  let sortKey = $state('');
  let sortDirection = $state<'asc' | 'desc'>('desc');

  // Toast notifications
  let toastMessage = $state('');
  let toastType = $state<'success' | 'error' | 'info'>('info');
  let showToast = $state(false);

  const jobColumns = [
    { key: 'name', label: 'Extraction', sortable: true },
    { key: 'jobType', label: 'Type', sortable: true },
    { 
      key: 'status', 
      label: 'Status',
      sortable: true,
      render: (value: string) => {
        const variant = value === 'Completed' ? 'success' : value === 'Running' ? 'info' : 'error';
        const colors = {
          success: 'bg-green-100 text-green-800',
          info: 'bg-blue-100 text-blue-800',
          error: 'bg-red-100 text-red-800'
        };
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colors[variant]}">${value}</span>`;
      }
    },
    { 
      key: 'timeSpentMs', 
      label: 'Duration',
      sortable: true,
      render: (value: number) => {
        const seconds = Math.floor(value / 1000);
        const minutes = Math.floor(seconds / 60);
        const hours = Math.floor(minutes / 60);
        
        if (hours > 0) return `${hours}h ${minutes % 60}m`;
        if (minutes > 0) return `${minutes}m ${seconds % 60}s`;
        return `${seconds}s`;
      }
    },
    { 
      key: 'megaBytes', 
      label: 'Data Size',
      sortable: true,
      render: (value: number) => {
        if (value < 1) return `${(value * 1024).toFixed(1)} KB`;
        if (value < 1024) return `${value.toFixed(1)} MB`;
        return `${(value / 1024).toFixed(1)} GB`;
      }
    },
    { 
      key: 'startTime', 
      label: 'Started',
      sortable: true,
      render: (value: string) => new Date(value).toLocaleString()
    }
  ];

  const aggregatedColumns = [
    { key: 'extractionName', label: 'Extraction', sortable: true },
    { key: 'totalJobs', label: 'Total Jobs', sortable: true },
    { 
      key: 'totalSizeMB', 
      label: 'Total Data',
      sortable: true,
      render: (value: number) => {
        if (value < 1024) return `${value.toFixed(1)} MB`;
        return `${(value / 1024).toFixed(1)} GB`;
      }
    },
    { 
      key: 'completedJobs', 
      label: 'Completed',
      sortable: true,
      render: (value: number, row: ExtractionAggregatedDto) => {
        const percentage = row.totalJobs > 0 ? (value / row.totalJobs * 100).toFixed(1) : '0';
        return `${value} (${percentage}%)`;
      }
    },
    { 
      key: 'failedJobs', 
      label: 'Failed',
      sortable: true,
      render: (value: number, row: ExtractionAggregatedDto) => {
        const percentage = row.totalJobs > 0 ? (value / row.totalJobs * 100).toFixed(1) : '0';
        return `${value} (${percentage}%)`;
      }
    },
    { 
      key: 'lastEndTime', 
      label: 'Last Run',
      sortable: true,
      render: (value: string) => value ? new Date(value).toLocaleString() : '-'
    }
  ];

  function showToastMessage(message: string, type: 'success' | 'error' | 'info' = 'info') {
    toastMessage = message;
    toastType = type;
    showToast = true;
    setTimeout(() => showToast = false, 5000);
  }

  onMount(async () => {
    await loadJobs();
    // Set up auto-refresh for active jobs
    const interval = setInterval(async () => {
      if (activeView === 'active') {
        await loadActiveJobs();
      }
    }, 5000);

    return () => clearInterval(interval);
  });

  async function loadJobs() {
    await Promise.all([
      loadActiveJobs(),
      loadRecentJobs(),
      loadAggregatedJobs()
    ]);
    loading = false;
  }

  async function loadActiveJobs() {
    try {
      const response = await api.getActiveJobs();
      activeJobs = response.content || [];
    } catch (error) {
      console.error('Failed to load active jobs:', error);
    }
  }

  async function loadRecentJobs() {
    try {
      const filters: Record<string, string> = {
        relativeStart: relativeTime,
        take: pageSize.toString(),
        skip: ((currentPage - 1) * pageSize).toString()
      };
      
      if (searchTerm) filters.extractionName = searchTerm;
      if (filterStatus) filters.status = filterStatus;
      if (filterType) filters.type = filterType;
      if (sortKey) {
        filters.sortBy = sortKey;
        filters.sortDirection = sortDirection;
      }

      const response = await api.searchJobs(filters);
      recentJobs = response.content || [];
      
      // Calculate pagination
      totalItems = response.entityCount || 0;
      totalPages = Math.ceil(totalItems / pageSize);
    } catch (error) {
      console.error('Failed to load recent jobs:', error);
      showToastMessage('Failed to load recent jobs', 'error');
    }
  }

  async function loadAggregatedJobs() {
    try {
      const filters: Record<string, string> = {
        relativeStart: relativeTime,
        take: aggregatedPageSize.toString(),
        skip: ((aggregatedCurrentPage - 1) * aggregatedPageSize).toString()
      };

      if (sortKey) {
        filters.sortBy = sortKey;
        filters.sortDirection = sortDirection;
      }

      const response = await api.getAggregatedJobs(filters);
      aggregatedJobs = response.content || [];
      
      // Calculate pagination
      aggregatedTotalItems = response.entityCount || 0;
      aggregatedTotalPages = Math.ceil(aggregatedTotalItems / aggregatedPageSize);
    } catch (error) {
      console.error('Failed to load aggregated jobs:', error);
      showToastMessage('Failed to load aggregated jobs', 'error');
    }
  }

  async function clearAllJobs() {
    if (confirm('Are you sure you want to clear all job history? This action cannot be undone.')) {
      try {
        await api.clearJobs();
        await loadJobs();
        showToastMessage('Job history cleared successfully', 'success');
      } catch (error) {
        console.error('Failed to clear jobs:', error);
        showToastMessage('Failed to clear job history', 'error');
      }
    }
  }

  function handleRecentJobsPageChange(page: number) {
    currentPage = page;
    loadRecentJobs();
  }

  function handleRecentJobsPageSizeChange(newPageSize: number) {
    pageSize = newPageSize;
    currentPage = 1;
    loadRecentJobs();
  }

  function handleAggregatedJobsPageChange(page: number) {
    aggregatedCurrentPage = page;
    loadAggregatedJobs();
  }

  function handleAggregatedJobsPageSizeChange(newPageSize: number) {
    aggregatedPageSize = newPageSize;
    aggregatedCurrentPage = 1;
    loadAggregatedJobs();
  }

  function handleSort(key: string, direction: 'asc' | 'desc') {
    sortKey = key;
    sortDirection = direction;
    
    if (activeView === 'recent') {
      currentPage = 1;
      loadRecentJobs();
    } else if (activeView === 'aggregated') {
      aggregatedCurrentPage = 1;
      loadAggregatedJobs();
    }
  }

  async function refreshData() {
    loading = true;
    await loadJobs();
    showToastMessage('Data refreshed', 'success');
  }

  // Auto-refresh when filters change
  $effect(() => {
    if (activeView === 'recent') {
      currentPage = 1;
      loadRecentJobs();
    }
  });

  $effect(() => {
    if (activeView === 'aggregated') {
      aggregatedCurrentPage = 1;
      loadAggregatedJobs();
    }
  });
</script>

<svelte:head>
  <title>Jobs - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader 
    title="Jobs" 
    description="Monitor extraction job history and performance"
  >
    {#snippet actions()}
      <div class="flex space-x-3">
        <Button variant="secondary" onclick={refreshData} loading={loading}>
          <RefreshCw size={16} class="mr-2" />
          Refresh
        </Button>
        <Button variant="danger" onclick={clearAllJobs}>
          <Trash2 size={16} class="mr-2" />
          Clear History
        </Button>
      </div>
    {/snippet}
  </PageHeader>

  <!-- Summary Cards -->
  <div class="grid grid-cols-1 md:grid-cols-4 gap-6">
    <Card>
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <Clock class="h-8 w-8 text-blue-500" />
        </div>
        <div class="ml-4">
          <p class="text-sm font-medium text-supabase-gray-600">Active Jobs</p>
          <p class="text-2xl font-semibold text-supabase-gray-900">{activeJobs.length}</p>
        </div>
      </div>
    </Card>

    <Card>
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <CheckCircle class="h-8 w-8 text-green-500" />
        </div>
        <div class="ml-4">
          <p class="text-sm font-medium text-supabase-gray-600">Completed (24h)</p>
          <p class="text-2xl font-semibold text-supabase-gray-900">
            {recentJobs.filter(j => j.status === 'Completed').length}
          </p>
        </div>
      </div>
    </Card>

    <Card>
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <XCircle class="h-8 w-8 text-red-500" />
        </div>
        <div class="ml-4">
          <p class="text-sm font-medium text-supabase-gray-600">Failed (24h)</p>
          <p class="text-2xl font-semibold text-supabase-gray-900">
            {recentJobs.filter(j => j.status === 'Failed').length}
          </p>
        </div>
      </div>
    </Card>

    <Card>
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <BarChart3 class="h-8 w-8 text-purple-500" />
        </div>
        <div class="ml-4">
          <p class="text-sm font-medium text-supabase-gray-600">Data Processed (24h)</p>
          <p class="text-2xl font-semibold text-supabase-gray-900">
            {(recentJobs.reduce((sum, job) => sum + job.megaBytes, 0) / 1024).toFixed(1)} GB
          </p>
        </div>
      </div>
    </Card>
  </div>

  <!-- View Tabs -->
  <div class="bg-white shadow rounded-lg">
    <div class="border-b border-supabase-gray-200">
      <nav class="-mb-px flex space-x-8 px-6">
        <button
          class="py-4 px-1 border-b-2 font-medium text-sm transition-colors"
          class:border-supabase-green={activeView === 'active'}
          class:text-supabase-green={activeView === 'active'}
          class:border-transparent={activeView !== 'active'}
          class:text-supabase-gray-500={activeView !== 'active'}
          class:hover:text-supabase-gray-700={activeView !== 'active'}
          onclick={() => activeView = 'active'}
        >
          Active Jobs ({activeJobs.length})
        </button>
        <button
          class="py-4 px-1 border-b-2 font-medium text-sm transition-colors"
          class:border-supabase-green={activeView === 'recent'}
          class:text-supabase-green={activeView === 'recent'}
          class:border-transparent={activeView !== 'recent'}
          class:text-supabase-gray-500={activeView !== 'recent'}
          class:hover:text-supabase-gray-700={activeView !== 'recent'}
          onclick={() => activeView = 'recent'}
        >
          Recent Jobs
        </button>
        <button
          class="py-4 px-1 border-b-2 font-medium text-sm transition-colors"
          class:border-supabase-green={activeView === 'aggregated'}
          class:text-supabase-green={activeView === 'aggregated'}
          class:border-transparent={activeView !== 'aggregated'}
          class:text-supabase-gray-500={activeView !== 'aggregated'}
          class:hover:text-supabase-gray-700={activeView !== 'aggregated'}
          onclick={() => activeView = 'aggregated'}
        >
          Aggregated View
        </button>
      </nav>
    </div>

    <div class="p-6">
      {#if activeView === 'active'}
        <div class="space-y-4">
          {#if activeJobs.length === 0}
            <div class="text-center py-12">
              <Clock class="mx-auto h-12 w-12 text-supabase-gray-400" />
              <h3 class="mt-2 text-sm font-medium text-supabase-gray-900">No active jobs</h3>
              <p class="mt-1 text-sm text-supabase-gray-500">All extractions are currently idle.</p>
            </div>
          {:else}
            <Table
              columns={jobColumns}
              data={activeJobs}
              loading={false}
              emptyMessage="No active jobs"
            />
          {/if}
        </div>

      {:else if activeView === 'recent'}
        <div class="space-y-4">
          <!-- Filters for recent jobs -->
          <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
            <Input
              placeholder="Search by extraction name..."
              bind:value={searchTerm}
            />
            <Select
              placeholder="Filter by status"
              bind:value={filterStatus}
              options={[
                { value: '', label: 'All Statuses' },
                { value: 'Completed', label: 'Completed' },
                { value: 'Failed', label: 'Failed' },
                { value: 'Running', label: 'Running' }
              ]}
            />
            <Select
              placeholder="Filter by type"
              bind:value={filterType}
              options={[
                { value: '', label: 'All Types' },
                { value: 'Transfer', label: 'Transfer' },
                { value: 'Fetch', label: 'Fetch' }
              ]}
            />
            <Select
              placeholder="Time range"
              bind:value={relativeTime}
              options={[
                { value: '3600', label: 'Last Hour' },
                { value: '86400', label: 'Last 24 Hours' },
                { value: '604800', label: 'Last Week' },
                { value: '2592000', label: 'Last Month' }
              ]}
            />
          </div>

          <Table
            columns={jobColumns}
            data={recentJobs}
            {loading}
            emptyMessage="No jobs found for the selected criteria"
            onSort={handleSort}
            {sortKey}
            {sortDirection}
            pagination={{
              currentPage,
              totalPages,
              pageSize,
              totalItems,
              onPageChange: handleRecentJobsPageChange,
              onPageSizeChange: handleRecentJobsPageSizeChange
            }}
          />
        </div>

      {:else if activeView === 'aggregated'}
        <div class="space-y-4">
          <div class="flex justify-between items-center">
            <p class="text-sm text-supabase-gray-600">
              Showing aggregated statistics for the selected time period
            </p>
            <Select
              bind:value={relativeTime}
              options={[
                { value: '86400', label: 'Last 24 Hours' },
                { value: '604800', label: 'Last Week' },
                { value: '2592000', label: 'Last Month' },
                { value: '7776000', label: 'Last 3 Months' }
              ]}
            />
          </div>

          <Table
            columns={aggregatedColumns}
            data={aggregatedJobs}
            {loading}
            emptyMessage="No aggregated data available"
            onSort={handleSort}
            {sortKey}
            {sortDirection}
            pagination={{
              currentPage: aggregatedCurrentPage,
              totalPages: aggregatedTotalPages,
              pageSize: aggregatedPageSize,
              totalItems: aggregatedTotalItems,
              onPageChange: handleAggregatedJobsPageChange,
              onPageSizeChange: handleAggregatedJobsPageSizeChange
            }}
          />
        </div>
      {/if}
    </div>
  </div>
</div>

<!-- Toast Notifications -->
<Toast bind:show={showToast} type={toastType} message={toastMessage} />