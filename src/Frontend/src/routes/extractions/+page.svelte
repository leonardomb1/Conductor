<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api.js';
  import type { Extraction } from '$lib/types.js';
  import PageHeader from '$lib/components/layout/PageHeader.svelte';
  import Table from '$lib/components/ui/Table.svelte';
  import Button from '$lib/components/ui/Button.svelte';
  import Input from '$lib/components/ui/Input.svelte';
  import Select from '$lib/components/ui/Select.svelte';
  import Badge from '$lib/components/ui/Badge.svelte';
  import Modal from '$lib/components/ui/Modal.svelte';
  import { Plus, Play, Download, Eye, Edit, Trash2 } from '@lucide/svelte';

  let extractions = $state<Extraction[]>([]);
  let loading = $state(true);
  let searchTerm = $state('');
  let filterOrigin = $state('');
  let filterDestination = $state('');
  let filterSchedule = $state('');
  let showExecuteModal = $state(false);
  let executeType = $state<'transfer' | 'pull'>('transfer');
  let executeLoading = $state(false);
  let selectedExtractions = $state<number[]>([]);
  let currentPage = $state(1);
  let totalPages = $state(1);
  const pageSize = 20;

  const columns = [
    { key: 'id', label: 'ID', sortable: true, width: '80px' },
    { key: 'extractionName', label: 'Name', sortable: true },
    { 
      key: 'sourceType', 
      label: 'Type', 
      render: (value: string) => {
        const type = value || 'db';
        const variant = type === 'http' ? 'info' : type === 'db' ? 'success' : 'default';
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-${variant === 'info' ? 'blue' : variant === 'success' ? 'green' : 'gray'}-100 text-${variant === 'info' ? 'blue' : variant === 'success' ? 'green' : 'gray'}-800">${type}</span>`;
      }
    },
    { 
      key: 'origin', 
      label: 'Origin', 
      render: (value: any) => value?.originName || '-'
    },
    { 
      key: 'destination', 
      label: 'Destination', 
      render: (value: any) => value?.destinationName || '-'
    },
    { 
      key: 'schedule', 
      label: 'Schedule', 
      render: (value: any) => value?.scheduleName || '-'
    },
    {
      key: 'isIncremental',
      label: 'Incremental',
      render: (value: boolean) => {
        return value 
          ? '<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">Yes</span>'
          : '<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">No</span>';
      }
    },
    {
      key: 'actions',
      label: 'Actions',
      render: (value: any, row: Extraction) => {
        return `
          <div class="flex space-x-2">
            <button onclick="viewExtraction(${row.id})" class="text-blue-600 hover:text-blue-800" title="View">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"></path><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"></path></svg>
            </button>
            <button onclick="editExtraction(${row.id})" class="text-green-600 hover:text-green-800" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteExtraction(${row.id})" class="text-red-600 hover:text-red-800" title="Delete">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
            </button>
          </div>
        `;
      }
    }
  ];

  onMount(async () => {
    await loadExtractions();
  });

  async function loadExtractions() {
    try {
      loading = true;
      const filters: Record<string, string> = {
        take: pageSize.toString()
      };
      
      if (searchTerm) filters.contains = searchTerm;
      if (filterOrigin) filters.origin = filterOrigin;
      if (filterDestination) filters.destination = filterDestination;
      if (filterSchedule) filters.schedule = filterSchedule;

      const response = await api.getExtractions(filters);
      extractions = response.content || [];
      
      // Calculate pagination
      totalPages = Math.ceil((response.entityCount || extractions.length) / pageSize);
    } catch (error) {
      console.error('Failed to load extractions:', error);
      alert('Failed to load extractions. Please check your connection and try again.');
    } finally {
      loading = false;
    }
  }

  async function executeExtractions() {
    if (selectedExtractions.length === 0) {
      alert('Please select at least one extraction');
      return;
    }

    executeLoading = true;
    try {
      // Get extraction names for the selected IDs
      const selectedNames = selectedExtractions.map(id => 
        extractions.find(e => e.id === id)?.extractionName
      ).filter(Boolean);

      if (selectedNames.length === 0) {
        alert('No valid extractions selected');
        return;
      }

      const filters = { 
        contains: selectedNames.join(',')
      };

      if (executeType === 'transfer') {
        await api.executeTransfer(filters);
        alert('Transfer job started successfully');
      } else {
        await api.executePull(filters);
        alert('Pull job started successfully');
      }
      
      showExecuteModal = false;
      selectedExtractions = [];
    } catch (error) {
      console.error(`Failed to execute ${executeType}:`, error);
      alert(`Failed to start ${executeType} job: ${error.message}`);
    } finally {
      executeLoading = false;
    }
  }

  function toggleExtractionSelection(id: number) {
    if (selectedExtractions.includes(id)) {
      selectedExtractions = selectedExtractions.filter(eid => eid !== id);
    } else {
      selectedExtractions = [...selectedExtractions, id];
    }
  }

  function selectAllVisible() {
    const visibleIds = extractions.map(e => e.id);
    selectedExtractions = [...new Set([...selectedExtractions, ...visibleIds])];
  }

  function deselectAllVisible() {
    const visibleIds = extractions.map(e => e.id);
    selectedExtractions = selectedExtractions.filter(id => !visibleIds.includes(id));
  }

  function handlePageChange(page: number) {
    currentPage = page;
    loadExtractions();
  }

  // Global functions for table actions
  if (typeof window !== 'undefined') {
    (window as any).viewExtraction = (id: number) => {
      window.location.href = `/extractions/${id}`;
    };
    
    (window as any).editExtraction = (id: number) => {
      window.location.href = `/extractions/${id}/edit`;
    };
    
    (window as any).deleteExtraction = async (id: number) => {
      if (confirm('Are you sure you want to delete this extraction?')) {
        try {
          await api.deleteExtraction(id);
          await loadExtractions();
        } catch (error) {
          console.error('Failed to delete extraction:', error);
          alert('Failed to delete extraction');
        }
      }
    };
  }

  $effect(() => {
    loadExtractions();
  });
</script>

<svelte:head>
  <title>Extractions - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader 
    title="Extractions" 
    description="Manage your data extraction configurations"
  >
    {#snippet actions()}
      <div class="flex space-x-3">
        <Button
          variant="secondary"
          onclick={() => showExecuteModal = true}
          disabled={selectedExtractions.length === 0}
        >
          <Play size={16} class="mr-2" />
          Execute Selected ({selectedExtractions.length})
        </Button>
        <Button variant="primary" onclick={() => window.location.href = '/extractions/new'}>
          <Plus size={16} class="mr-2" />
          New Extraction
        </Button>
      </div>
    {/snippet}
  </PageHeader>

  <!-- Filters -->
  <div class="bg-white p-4 rounded-lg shadow">
    <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
      <Input
        placeholder="Search extractions..."
        bind:value={searchTerm}
      />
      <Input
        placeholder="Filter by origin..."
        bind:value={filterOrigin}
      />
      <Input
        placeholder="Filter by destination..."
        bind:value={filterDestination}
      />
      <Input
        placeholder="Filter by schedule..."
        bind:value={filterSchedule}
      />
    </div>
  </div>

  <!-- Selection Info -->
  {#if selectedExtractions.length > 0}
    <div class="bg-blue-50 border border-blue-200 rounded-md p-4">
      <div class="flex items-center justify-between">
        <span class="text-sm text-blue-700">
          {selectedExtractions.length} extraction{selectedExtractions.length !== 1 ? 's' : ''} selected
        </span>
        <div class="flex space-x-2">
          <button
            onclick={selectAllVisible}
            class="text-sm text-blue-600 hover:text-blue-800"
          >
            Select all visible
          </button>
          <button
            onclick={deselectAllVisible}
            class="text-sm text-blue-600 hover:text-blue-800"
          >
            Deselect all visible
          </button>
          <button
            onclick={() => selectedExtractions = []}
            class="text-sm text-blue-600 hover:text-blue-800"
          >
            Clear all
          </button>
        </div>
      </div>
    </div>
  {/if}

  <!-- Extractions Table -->
  <div class="bg-white shadow rounded-lg">
    <div class="p-6">
      <Table
        {columns}
        data={extractions}
        {loading}
        emptyMessage="No extractions found"
        pagination={{
          currentPage,
          totalPages,
          pageSize,
          totalItems: extractions.length,
          onPageChange: handlePageChange
        }}
      />
    </div>
  </div>

  <!-- Bulk selection checkboxes -->
  {#if extractions.length > 0}
    <div class="bg-white p-4 rounded-lg shadow">
      <div class="flex justify-between items-center mb-3">
        <h4 class="text-sm font-medium text-supabase-gray-700">Bulk Operations</h4>
        <div class="flex space-x-2">
          <Button size="sm" variant="ghost" onclick={selectAllVisible}>
            Select All Visible
          </Button>
          <Button size="sm" variant="ghost" onclick={deselectAllVisible}>
            Deselect All Visible
          </Button>
        </div>
      </div>
      <div class="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 gap-2">
        {#each extractions as extraction}
          <label class="flex items-center space-x-2 text-sm">
            <input
              type="checkbox"
              checked={selectedExtractions.includes(extraction.id)}
              onchange={() => toggleExtractionSelection(extraction.id)}
              class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
            />
            <span class="truncate">{extraction.extractionName}</span>
          </label>
        {/each}
      </div>
    </div>
  {/if}
</div>

<!-- Execute Modal -->
<Modal bind:open={showExecuteModal} title="Execute Extractions">
  <div class="space-y-4">
    <p class="text-sm text-supabase-gray-600">
      Execute {selectedExtractions.length} selected extraction{selectedExtractions.length !== 1 ? 's' : ''}.
    </p>

    <div class="bg-supabase-gray-50 p-3 rounded-md">
      <h5 class="text-sm font-medium text-supabase-gray-700 mb-2">Selected extractions:</h5>
      <div class="text-sm text-supabase-gray-600">
        {#each selectedExtractions as id}
          <div>• {extractions.find(e => e.id === id)?.extractionName || `ID: ${id}`}</div>
        {/each}
      </div>
    </div>

    <Select
      label="Execution Type"
      bind:value={executeType}
      options={[
        { value: 'transfer', label: 'Transfer (to destination)' },
        { value: 'pull', label: 'Pull (to CSV)' }
      ]}
    />

    <div class="flex justify-end space-x-3">
      <Button variant="secondary" onclick={() => showExecuteModal = false}>
        Cancel
      </Button>
      <Button 
        variant="primary" 
        loading={executeLoading}
        onclick={executeExtractions}
      >
        Execute {executeType}
      </Button>
    </div>
  </div>
</Modal>