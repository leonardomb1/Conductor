<script lang="ts">
  import { onMount } from 'svelte';
  import { Card, CardContent, CardHeader, CardTitle } from '$lib/components/ui/Card.svelte';
  import { Button } from '$lib/components/ui/Button.svelte';
  import { Badge } from '$lib/components/ui/Badge.svelte';
  import { Tabs, TabsContent, TabsList, TabsTrigger } from '$lib/components/ui/Tabs.svelte';
  import { Alert, AlertDescription } from '$lib/components/ui/Alert.svelte';
  import { Input } from '$lib/components/ui/Input.svelte';
  import { Label } from '$lib/components/ui/Label.svelte';
  import { DataTable } from '$lib/components/DataTable.svelte';
  import { EntityForm } from '$lib/components/EntityForm.svelte';
  import { MetricsDashboard } from '$lib/components/MetricsDashboard.svelte';
  import { JobsDashboard } from '$lib/components/JobsDashboard.svelte';
  import { JobExecutionModal } from '$lib/components/JobExecutionModal.svelte';
  import { FetchModal } from '$lib/components/FetchModal.svelte';
  import { AuthService, isAuthenticated } from '$lib/auth';
  import type { ApiResponse, Origin, Destination, Schedule, User, Extraction, JobDto } from '$lib/types';

  let activeTab = 'overview';
  let origins: Origin[] = [];
  let destinations: Destination[] = [];
  let schedules: Schedule[] = [];
  let users: User[] = [];
  let extractions: Extraction[] = [];
  let activeJobs = '';
  let recentJobs: JobDto[] = [];
  let loading = false;
  let error = '';
  let showForm: { type: string; item?: any } | null = null;
  let jobModal: { type: 'transfer' | 'pull' } | null = null;
  let fetchModal = false;
  let editMode = false;

  // Pagination state
  let currentPages = {
    origins: 1,
    destinations: 1,
    schedules: 1,
    users: 1,
    extractions: 1,
    jobs: 1
  };
  
  let totalPages = {
    origins: 1,
    destinations: 1,
    schedules: 1,
    users: 1,
    extractions: 1,
    jobs: 1
  };

  const pageSize = 10;

  // Redirect if not authenticated
  const redirectIfNotAuthenticated = $derived(isAuthenticated, ($isAuthenticated) => {
    if (!$isAuthenticated) {
      window.location.href = '/';
    }
  });

  const loadData = async () => {
    if (!isAuthenticated) return;

    loading = true;
    error = '';
    try {
      await Promise.all([
        loadPaginatedData('origins'),
        loadPaginatedData('destinations'),
        loadPaginatedData('schedules'),
        loadPaginatedData('users'),
        loadPaginatedData('extractions'),
      ]);

      // Load active jobs
      const activeJobsResponse = await AuthService.authenticatedFetch('/api/jobs/active');
      const activeJobsData = await activeJobsResponse.json();
      activeJobs = activeJobsData.information || 'No active jobs';

      // Load recent jobs
      await loadPaginatedData('jobs');
    } catch (err) {
      if (err instanceof Error && err.message !== 'Authentication expired') {
        error = 'Failed to load data';
      }
    } finally {
      loading = false;
    }
  };

  const loadPaginatedData = async (type: string, page = 1) => {
    try {
      const endpoint = type === 'jobs' ? '/api/jobs/search' : `/api/${type}`;
      const params = new URLSearchParams({
        page: page.toString(),
        size: pageSize.toString()
      });

      if (type === 'jobs') {
        params.append('take', '10');
      }

      const response = await AuthService.authenticatedFetch(`${endpoint}?${params.toString()}`);
      if (!response.ok) throw new Error(`Failed to fetch ${type}`);

      const data: ApiResponse<any> = await response.json();
      const content = data.content || [];
      const total = data.entityCount || content.length;
      
      currentPages[type as keyof typeof currentPages] = page;
      totalPages[type as keyof typeof totalPages] = Math.ceil(total / pageSize);

      switch (type) {
        case 'origins':
          origins = content;
          break;
        case 'destinations':
          destinations = content;
          break;
        case 'schedules':
          schedules = content;
          break;
        case 'users':
          users = content;
          break;
        case 'extractions':
          extractions = content;
          break;
        case 'jobs':
          recentJobs = content;
          break;
      }
    } catch (err) {
      if (err instanceof Error && err.message !== 'Authentication expired') {
        throw err;
      }
    }
  };

  const handlePageChange = (type: string, page: number) => {
    loadPaginatedData(type, page);
  };

  const handleDelete = async (type: string, id: number) => {
    try {
      const response = await AuthService.authenticatedFetch(`/api/${type}/${id}`, {
        method: 'DELETE',
      });
      if (response.ok) {
        loadPaginatedData(type, currentPages[type as keyof typeof currentPages]);
      }
    } catch (err) {
      if (err instanceof Error && err.message !== 'Authentication expired') {
        error = `Failed to delete ${type}`;
      }
    }
  };

  const handleSave = async (type: string, data: any) => {
    try {
      const isEdit = data.id;
      const endpoint = isEdit ? `/api/${type}/${data.id}` : `/api/${type}`;
      const method = isEdit ? 'PUT' : 'POST';

      const response = await AuthService.authenticatedFetch(endpoint, {
        method,
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(data),
      });

      if (response.ok) {
        showForm = null;
        loadPaginatedData(type, currentPages[type as keyof typeof currentPages]);
      }
    } catch (err) {
      if (err instanceof Error && err.message !== 'Authentication expired') {
        error = `Failed to save ${type}`;
      }
    }
  };

  const executeJob = async (type: 'transfer' | 'pull', params: Record<string, string>) => {
    try {
      // Build query string from parameters
      const queryParams = new URLSearchParams();
      Object.entries(params).forEach(([key, value]) => {
        if (value) {
          queryParams.append(key, value);
        }
      });

      const queryString = queryParams.toString();
      const endpoint = `/api/extractions/${type}${queryString ? `?${queryString}` : ''}`;

      const response = await AuthService.authenticatedFetch(endpoint, {
        method: 'POST',
      });

      const result = await response.json();

      // Show result message
      if (response.status === 202) {
        alert(`Job accepted: ${result.information}`);
      } else if (response.ok) {
        alert(`Job completed: ${result.information}`);
      } else {
        throw new Error(result.information || 'Job execution failed');
      }

      loadData();
    } catch (err) {
      if (err instanceof Error && err.message !== 'Authentication expired') {
        throw err; // Re-throw to be handled by the modal
      }
    }
  };

  const handleLogout = () => {
    AuthService.logout();
  };

  onMount(() => {
    loadData();
  });
\
  const OverviewCards = () => (
    <div class=\"grid gap-4 md:grid-cols-2 lg:grid-cols-4">\
      <Card>\
        <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">\
          <CardTitle class="text-sm font-medium\">Origins</CardTitle>\
          <svg class=\"h-4 w-4 text-muted-foreground\" fill="none\" stroke=\"currentColor" viewBox="0 0 24 24\">\
            <path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width="2" d=\"M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4\" />\
          </svg>\
        </CardHeader>\
        <CardContent>\
          <div class=\"text-2xl font-bold">{origins.length}</div>
        </CardContent>
      </Card>
      <Card>\
        <CardHeader class=\"flex flex-row items-center justify-between space-y-0 pb-2">\
          <CardTitle class="text-sm font-medium\">Destinations</CardTitle>\
          <svg class=\"h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
          </svg>
        </CardHeader>
        <CardContent>
          <div class="text-2xl font-bold">{destinations.length}</div>
        </CardContent>
      </Card>
      <Card>
        <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
          <CardTitle class="text-sm font-medium">Extractions</CardTitle>
          <svg class="h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
          </svg>
        </CardHeader>
        <CardContent>
          <div class="text-2xl font-bold">{extractions.length}</div>
        </CardContent>
      </Card>
      <Card>
        <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
          <CardTitle class="text-sm font-medium">Active Jobs</CardTitle>
          <svg class="h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
          </svg>
        </CardHeader>
        <CardContent>
          <div class="text-sm">{activeJobs}</div>
        </CardContent>
      </Card>
    </div>
  );

  const EntityManager = ({ title, data, type, currentPage, totalPage, hideColumns = [], onEdit, onDelete }: {
    title: string;
    data: any[];
    type: string;
    currentPage: number;
    totalPage: number;
    hideColumns?: string[];
    onEdit?: (item: any) => void;
    onDelete?: (item: any) => void;
  }) => (
    <Card>
      <CardHeader>
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-2">
            <CardTitle>{title}</CardTitle>
          </div>
          <div class="flex gap-2">
            {#if (type === 'origins' || type === 'destinations') && data.length > 0}
              <Button
                variant="outline"
                size="sm"
                on:click={() => editMode = !editMode}
              >
                <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" />
                </svg>
                {editMode ? 'View' : 'Edit'}
              </Button>
            {/if}
            <Button on:click={() => showForm = { type }}>
              <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
              </svg>
              Add {title.slice(0, -1)}
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <DataTable
          {data}
          onEdit={onEdit}
          onDelete={onDelete}
          {currentPage}
          totalPages={totalPage}
          onPageChange={(page) => handlePageChange(type, page)}
          {hideColumns}
          {editMode}
        />
      </CardContent>
    </Card>
  );

  const clearAllJobs = async () => {
    if (confirm('Clear all job records?')) {
      try {
        await AuthService.authenticatedFetch('/api/jobs', { method: 'DELETE' });
        loadData();
      } catch (err) {
        if (err instanceof Error && err.message !== 'Authentication expired') {
          error = 'Failed to clear jobs';
        }
      }
    }
  };
</script>

<div class="min-h-screen bg-background">
  <header class="bg-card shadow-sm border-b">
    <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
      <div class="flex justify-between items-center py-4">
        <h1 class="text-2xl font-bold text-foreground">Conductor Dashboard</h1>
        <div class="flex gap-2">
          <Button on:click={() => jobModal = { type: 'transfer' }} variant="outline">
            <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
            </svg>
            Transfer
          </Button>
          <Button on:click={() => jobModal = { type: 'pull' }} variant="outline">
            <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
            Pull
          </Button>
          <Button on:click={() => fetchModal = true} variant="outline">
            <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            Fetch
          </Button>
          <Button on:click={handleLogout} variant="outline">
            <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
            </svg>
            Logout
          </Button>
        </div>
      </div>
    </div>
  </header>

  <main class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
    {#if error}
      <Alert variant="destructive" class="mb-6">
        <AlertDescription>{error}</AlertDescription>
      </Alert>
    {/if}

    <Tabs bind:value={activeTab}>
      <TabsList class="grid w-full grid-cols-9">
        <TabsTrigger value="overview">Overview</TabsTrigger>
        <TabsTrigger value="metrics">
          <svg class="h-4 w-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
          </svg>
          Metrics
        </TabsTrigger>
        <TabsTrigger value="jobs-analytics">
          <svg class="h-4 w-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" />
          </svg>
          Jobs
        </TabsTrigger>
        <TabsTrigger value="origins">Origins</TabsTrigger>
        <TabsTrigger value="destinations">Destinations</TabsTrigger>
        <TabsTrigger value="schedules">Schedules</TabsTrigger>
        <TabsTrigger value="extractions">Extractions</TabsTrigger>
        <TabsTrigger value="users">Users</TabsTrigger>
        <TabsTrigger value="jobs">Jobs</TabsTrigger>
      </TabsList>

      <TabsContent value="overview" class="space-y-6">
        <OverviewCards />

        <Card>
          <CardHeader>
            <CardTitle>Recent Jobs</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="space-y-2">
              {#each recentJobs.slice(0, 5) as job}
                <div class="flex items-center justify-between p-2 border rounded">
                  <div>
                    <span class="font-medium">{job.name}</span>
                    <Badge variant={job.status === 'Completed' ? 'default' : 'secondary'} class="ml-2">
                      {job.status}
                    </Badge>
                  </div>
                  <div class="text-sm text-muted-foreground">{new Date(job.startTime).toLocaleString()}</div>
                </div>
              {/each}
            </div>
          </CardContent>
        </Card>
      </TabsContent>

      <TabsContent value="metrics">
        <MetricsDashboard />
      </TabsContent>

      <TabsContent value="jobs-analytics">
        <JobsDashboard />
      </TabsContent>

      <TabsContent value="origins">
        <EntityManager
          title="Origins"
          data={origins}
          type="origins"
          currentPage={currentPages.origins}
          totalPage={totalPages.origins}
          hideColumns={editMode ? [] : ['originConStr']}
          onEdit={(item) => showForm = { type, item }}
          onDelete={(item) => handleDelete('origins', item.id)}
        />
      </TabsContent>

      <TabsContent value="destinations">
        <EntityManager
          title="Destinations"
          data={destinations}
          type="destinations"
          currentPage={currentPages.destinations}
          totalPage={totalPages.destinations}
          hideColumns={editMode ? [] : ['destinationConStr']}
          onEdit={(item) => showForm = { type, item }}
          onDelete={(item) => handleDelete('destinations', item.id)}
        />
      </TabsContent>

      <TabsContent value="schedules">
        <EntityManager
          title="Schedules"
          data={schedules}
          type="schedules"
          currentPage={currentPages.schedules}
          totalPage={totalPages.schedules}
          onEdit={(item) => showForm = { type, item }}
          onDelete={(item) => handleDelete('schedules', item.id)}
        />
      </TabsContent>

      <TabsContent value="extractions">
        <EntityManager
          title="Extractions"
          data={extractions}
          type="extractions"
          currentPage={currentPages.extractions}
          totalPage={totalPages.extractions}
          onEdit={(item) => showForm = { type, item }}
          onDelete={(item) => handleDelete('extractions', item.id)}
        />
      </TabsContent>

      <TabsContent value="users">
        <EntityManager
          title="Users"
          data={users}
          type="users"
          currentPage={currentPages.users}
          totalPage={totalPages.users}
          onEdit={(item) => showForm = { type, item }}
          onDelete={(item) => handleDelete('users', item.id)}
        />
      </TabsContent>

      <TabsContent value="jobs">
        <Card>
          <CardHeader>
            <CardTitle>Job Management</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="space-y-4">
              <div>
                <h3 class="font-medium mb-2">Active Jobs</h3>
                <p class="text-sm text-muted-foreground">{activeJobs}</p>
              </div>

              <div>
                <h3 class="font-medium mb-2">Recent Jobs</h3>
                <DataTable
                  data={recentJobs}
                  currentPage={currentPages.jobs}
                  totalPages={totalPages.jobs}
                  onPageChange={(page) => handlePageChange('jobs', page)}
                />
              </div>

              <Button
                variant="destructive"
                on:click={clearAllJobs}
              >
                <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
                Clear All Jobs
              </Button>
            </div>
          </CardContent>
        </Card>
      </TabsContent>
    </Tabs>
  </main>

  {#if showForm}
    <EntityForm
      type={showForm.type}
      item={showForm.item}
      onSave={(data) => handleSave(showForm.type, data)}
      onCancel={() => showForm = null}
      {origins}
      {destinations}
      {schedules}
    />
  {/if}

  {#if jobModal}
    <JobExecutionModal
      type={jobModal.type}
      isOpen={true}
      onClose={() => jobModal = null}
      onExecute={(params) => executeJob(jobModal.type, params)}
    />
  {/if}

  {#if fetchModal}
    <FetchModal
      isOpen={true}
      onClose={() => fetchModal = false}
    />
  {/if}
</div>
