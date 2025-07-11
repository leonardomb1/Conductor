<script lang="ts">
  import { page } from '$app/stores';
  import { onMount } from 'svelte';
  import { api } from '$lib/api.js';
  import type { Extraction } from '$lib/types.js';
  import PageHeader from '$lib/components/layout/PageHeader.svelte';
  import Card from '$lib/components/ui/Card.svelte';
  import Button from '$lib/components/ui/Button.svelte';
  import Badge from '$lib/components/ui/Badge.svelte';
  import Table from '$lib/components/ui/Table.svelte';
  import Modal from '$lib/components/ui/Modal.svelte';
  import { PencilSimple, Play, Download, Eye, ArrowLeft } from 'phosphor-svelte';

  let extraction = $state<Extraction | null>(null);
  let loading = $state(true);
  let previewData = $state<any[]>([]);
  let previewLoading = $state(false);
  let showPreviewModal = $state(false);
  let previewColumns = $state<any[]>([]);

  const extractionId = $derived(+$page.params.id);

  onMount(async () => {
    await loadExtraction();
  });

  async function loadExtraction() {
    try {
      loading = true;
      const response = await api.getExtraction(extractionId);
      extraction = response.content?.[0] || null;
    } catch (error) {
      console.error('Failed to load extraction:', error);
    } finally {
      loading = false;
    }
  }

  async function fetchPreview() {
    if (!extraction) return;

    try {
      previewLoading = true;
      const response = await api.fetchData({ 
        name: extraction.extractionName,
        page: '1'
      });
      
      previewData = response.content || [];
      
      if (previewData.length > 0) {
        previewColumns = Object.keys(previewData[0]).map(key => ({
          key,
          label: key,
          sortable: false
        }));
      }
      
      showPreviewModal = true;
    } catch (error) {
      console.error('Failed to fetch preview:', error);
      alert('Failed to fetch preview data');
    } finally {
      previewLoading = false;
    }
  }

  async function executeTransfer() {
    if (!extraction) return;

    try {
      await api.executeTransfer({ name: extraction.extractionName });
      alert('Transfer job started successfully');
    } catch (error) {
      console.error('Failed to execute transfer:', error);
      alert('Failed to start transfer job');
    }
  }

  async function executePull() {
    if (!extraction) return;

    try {
      await api.executePull({ name: extraction.extractionName });
      alert('Pull job started successfully');
    } catch (error) {
      console.error('Failed to execute pull:', error);
      alert('Failed to start pull job');
    }
  }
</script>

<svelte:head>
  <title>{extraction?.extractionName || 'Loading...'} - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader 
    title={extraction?.extractionName || 'Loading...'}
    description="Extraction configuration details"
  >
    {#snippet actions()}
      <div class="flex space-x-3">
        <Button variant="ghost" onclick={() => history.back()}>
          <ArrowLeft size={16} class="mr-2" />
          Back
        </Button>
        {#if extraction}
          <Button variant="secondary" onclick={fetchPreview} loading={previewLoading}>
            <Eye size={16} class="mr-2" />
            Preview
          </Button>
          <Button variant="secondary" onclick={executePull}>
            <Download size={16} class="mr-2" />
            Pull
          </Button>
          <Button variant="primary" onclick={executeTransfer}>
            <Play size={16} class="mr-2" />
            Transfer
          </Button>
          <Button variant="secondary" onclick={() => window.location.href = `/extractions/${extractionId}/edit`}>
            <PencilSimple size={16} class="mr-2" />
            Edit
          </Button>
        {/if}
      </div>
    {/snippet}
  </PageHeader>

  {#if loading}
    <div class="flex justify-center py-12">
      <svg class="animate-spin h-8 w-8 text-supabase-gray-500" fill="none" viewBox="0 0 24 24">
        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
        <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
      </svg>
    </div>
  {:else if !extraction}
    <Card>
      <div class="text-center py-12">
        <p class="text-supabase-gray-500">Extraction not found</p>
      </div>
    </Card>
  {:else}
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <!-- Main Info -->
      <div class="lg:col-span-2 space-y-6">
        <Card title="Basic Information">
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Name</label>
              <p class="mt-1 text-sm text-supabase-gray-900">{extraction.extractionName}</p>
            </div>
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Alias</label>
              <p class="mt-1 text-sm text-supabase-gray-900">{extraction.extractionAlias || '-'}</p>
            </div>
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Source Type</label>
              <p class="mt-1">
                <Badge variant={extraction.sourceType === 'http' ? 'info' : 'success'}>
                  {extraction.sourceType || 'db'}
                </Badge>
              </p>
            </div>
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Index Name</label>
              <p class="mt-1 text-sm text-supabase-gray-900">{extraction.indexName || '-'}</p>
            </div>
          </div>
        </Card>

        <Card title="Configuration">
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Incremental</label>
              <p class="mt-1">
                <Badge variant={extraction.isIncremental ? 'success' : 'default'}>
                  {extraction.isIncremental ? 'Yes' : 'No'}
                </Badge>
              </p>
            </div>
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Virtual</label>
              <p class="mt-1">
                <Badge variant={extraction.isVirtual ? 'success' : 'default'}>
                  {extraction.isVirtual ? 'Yes' : 'No'}
                </Badge>
              </p>
            </div>
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Filter Time</label>
              <p class="mt-1 text-sm text-supabase-gray-900">{extraction.filterTime || '-'}</p>
            </div>
          </div>
        </Card>

        {#if extraction.sourceType === 'http'}
          <Card title="HTTP Configuration">
            <div class="space-y-4">
              <div>
                <label class="block text-sm font-medium text-supabase-gray-700">Endpoint</label>
                <p class="mt-1 text-sm text-supabase-gray-900 break-all">{extraction.endpointFullName || '-'}</p>
              </div>
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label class="block text-sm font-medium text-supabase-gray-700">HTTP Method</label>
                  <p class="mt-1">
                    <Badge variant="info">{extraction.httpMethod || 'GET'}</Badge>
                  </p>
                </div>
                <div>
                  <label class="block text-sm font-medium text-supabase-gray-700">Pagination Type</label>
                  <p class="mt-1 text-sm text-supabase-gray-900">{extraction.paginationType || '-'}</p>
                </div>
              </div>
            </div>
          </Card>
        {/if}

        {#if extraction.overrideQuery}
          <Card title="Override Query">
            <pre class="text-sm text-supabase-gray-900 bg-supabase-gray-50 p-4 rounded-md overflow-x-auto">{extraction.overrideQuery}</pre>
          </Card>
        {/if}

        {#if extraction.script}
          <Card title="Custom Script">
            <pre class="text-sm text-supabase-gray-900 bg-supabase-gray-50 p-4 rounded-md overflow-x-auto">{extraction.script}</pre>
          </Card>
        {/if}
      </div>

      <!-- Relations -->
      <div class="space-y-6">
        <Card title="Relations">
          <div class="space-y-4">
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Origin</label>
              <p class="mt-1 text-sm text-supabase-gray-900">
                {extraction.origin?.originName || '-'}
              </p>
            </div>
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Destination</label>
              <p class="mt-1 text-sm text-supabase-gray-900">
                {extraction.destination?.destinationName || '-'}
              </p>
            </div>
            <div>
              <label class="block text-sm font-medium text-supabase-gray-700">Schedule</label>
              <p class="mt-1 text-sm text-supabase-gray-900">
                {extraction.schedule?.scheduleName || '-'}
              </p>
            </div>
          </div>
        </Card>

        {#if extraction.filterColumn}
          <Card title="Filtering">
            <div class="space-y-3">
              <div>
                <label class="block text-sm font-medium text-supabase-gray-700">Filter Column</label>
                <p class="mt-1 text-sm text-supabase-gray-900">{extraction.filterColumn}</p>
              </div>
              {#if extraction.filterCondition}
                <div>
                  <label class="block text-sm font-medium text-supabase-gray-700">Filter Condition</label>
                  <p class="mt-1 text-sm text-supabase-gray-900">{extraction.filterCondition}</p>
                </div>
              {/if}
            </div>
          </Card>
        {/if}

        {#if extraction.dependencies}
          <Card title="Dependencies">
            <div class="space-y-2">
              {#each extraction.dependencies.split(',') as dep}
                <span class="inline-block px-2 py-1 text-xs bg-supabase-gray-100 text-supabase-gray-800 rounded">
                  {dep.trim()}
                </span>
              {/each}
            </div>
          </Card>
        {/if}
      </div>
    </div>
  {/if}
</div>

<!-- Preview Modal -->
<Modal bind:open={showPreviewModal} title="Data Preview" size="xl">
  <div class="space-y-4">
    {#if previewData.length > 0}
      <p class="text-sm text-supabase-gray-600">
        Showing first {previewData.length} rows
      </p>
      <Table
        columns={previewColumns}
        data={previewData}
        loading={false}
        emptyMessage="No data available"
      />
    {:else}
      <p class="text-center py-8 text-supabase-gray-500">No data available for preview</p>
    {/if}
  </div>
</Modal>