<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api.js';
  import type { Origin } from '$lib/types.js';
  import PageHeader from '$lib/components/layout/PageHeader.svelte';
  import Table from '$lib/components/ui/Table.svelte';
  import Button from '$lib/components/ui/Button.svelte';
  import Input from '$lib/components/ui/Input.svelte';
  import Modal from '$lib/components/ui/Modal.svelte';
  import Card from '$lib/components/ui/Card.svelte';
  import Select from '$lib/components/ui/Select.svelte';
  import { Plus, PencilSimple, Trash, Database } from 'phosphor-svelte';

  let origins = $state<Origin[]>([]);
  let loading = $state(true);
  let searchTerm = $state('');
  let showModal = $state(false);
  let modalMode = $state<'create' | 'edit'>('create');
  let selectedOrigin = $state<Origin | null>(null);
  let saving = $state(false);

  // Form data
  let formData = $state({
    originName: '',
    originAlias: '',
    originDbType: '',
    originConStr: '',
    originTimeZoneOffSet: 0
  });

  let errors = $state<Record<string, string>>({});

  const columns = [
    { key: 'id', label: 'ID', sortable: true, width: '80px' },
    { key: 'originName', label: 'Name', sortable: true },
    { key: 'originAlias', label: 'Alias', render: (value: string) => value || '-' },
    { 
      key: 'originDbType', 
      label: 'Database Type',
      render: (value: string) => {
        if (!value) return '-';
        const colors = {
          'PostgreSQL': 'bg-blue-100 text-blue-800',
          'MySQL': 'bg-orange-100 text-orange-800',
          'SqlServer': 'bg-green-100 text-green-800'
        };
        const colorClass = colors[value as keyof typeof colors] || 'bg-gray-100 text-gray-800';
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colorClass}">${value}</span>`;
      }
    },
    { key: 'originTimeZoneOffSet', label: 'Timezone Offset', render: (value: number) => value ? `${value > 0 ? '+' : ''}${value}` : '0' },
    {
      key: 'actions',
      label: 'Actions',
      render: (value: any, row: Origin) => {
        return `
          <div class="flex space-x-2">
            <button onclick="editOrigin(${row.id})" class="text-green-600 hover:text-green-800" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteOrigin(${row.id})" class="text-red-600 hover:text-red-800" title="Delete">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
            </button>
          </div>
        `;
      }
    }
  ];

  onMount(async () => {
    await loadOrigins();
  });

  async function loadOrigins() {
    try {
      loading = true;
      const filters: Record<string, string> = {};
      if (searchTerm) filters.name = searchTerm;

      const response = await api.getOrigins(filters);
      origins = response.content || [];
    } catch (error) {
      console.error('Failed to load origins:', error);
    } finally {
      loading = false;
    }
  }

  function openCreateModal() {
    modalMode = 'create';
    selectedOrigin = null;
    formData = {
      originName: '',
      originAlias: '',
      originDbType: '',
      originConStr: '',
      originTimeZoneOffSet: 0
    };
    errors = {};
    showModal = true;
  }

  function openEditModal(origin: Origin) {
    modalMode = 'edit';
    selectedOrigin = origin;
    formData = {
      originName: origin.originName,
      originAlias: origin.originAlias || '',
      originDbType: origin.originDbType || '',
      originConStr: '••••••••', // Don't show actual connection string
      originTimeZoneOffSet: origin.originTimeZoneOffSet || 0
    };
    errors = {};
    showModal = true;
  }

  function validateForm(): boolean {
    errors = {};

    if (!formData.originName.trim()) {
      errors.originName = 'Name is required';
    }

    if (!formData.originDbType) {
      errors.originDbType = 'Database type is required';
    }

    if (!formData.originConStr.trim()) {
      errors.originConStr = 'Connection string is required';
    }

    return Object.keys(errors).length === 0;
  }

  async function handleSubmit() {
    if (!validateForm()) return;

    try {
      saving = true;
      
      const originData = {
        originName: formData.originName,
        originAlias: formData.originAlias || undefined,
        originDbType: formData.originDbType,
        originConStr: formData.originConStr,
        originTimeZoneOffSet: formData.originTimeZoneOffSet
      };

      if (modalMode === 'create') {
        await api.createOrigin(originData);
      } else if (selectedOrigin) {
        await api.updateOrigin(selectedOrigin.id, originData);
      }

      showModal = false;
      await loadOrigins();
    } catch (error) {
      console.error(`Failed to ${modalMode} origin:`, error);
      alert(`Failed to ${modalMode} origin`);
    } finally {
      saving = false;
    }
  }

  // Global functions for table actions
  if (typeof window !== 'undefined') {
    (window as any).editOrigin = (id: number) => {
      const origin = origins.find(o => o.id === id);
      if (origin) openEditModal(origin);
    };
    
    (window as any).deleteOrigin = async (id: number) => {
      if (confirm('Are you sure you want to delete this origin?')) {
        try {
          await api.deleteOrigin(id);
          await loadOrigins();
        } catch (error) {
          console.error('Failed to delete origin:', error);
          alert('Failed to delete origin');
        }
      }
    };
  }

  $effect(() => {
    loadOrigins();
  });
</script>

<svelte:head>
  <title>Origins - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader 
    title="Origins" 
    description="Manage data source connections"
  >
    {#snippet actions()}
      <Button variant="primary" onclick={openCreateModal}>
        <Plus size={16} class="mr-2" />
        New Origin
      </Button>
    {/snippet}
  </PageHeader>

  <!-- Filters -->
  <div class="bg-white p-4 rounded-lg shadow">
    <div class="max-w-md">
      <Input
        placeholder="Search origins..."
        bind:value={searchTerm}
      />
    </div>
  </div>

  <!-- Origins Table -->
  <div class="bg-white shadow rounded-lg">
    <div class="p-6">
      <Table
        {columns}
        data={origins}
        {loading}
        emptyMessage="No origins found"
      />
    </div>
  </div>
</div>

<!-- Create/Edit Modal -->
<Modal bind:open={showModal} title={modalMode === 'create' ? 'New Origin' : 'Edit Origin'}>
  <form onsubmit|preventDefault={handleSubmit} class="space-y-4">
    <Input
      label="Name"
      bind:value={formData.originName}
      error={errors.originName}
      required
      placeholder="Enter origin name"
    />

    <Input
      label="Alias"
      bind:value={formData.originAlias}
      placeholder="Optional alias"
    />

    <Select
      label="Database Type"
      bind:value={formData.originDbType}
      error={errors.originDbType}
      required
      placeholder="Select database type"
      options={[
        { value: 'PostgreSQL', label: 'PostgreSQL' },
        { value: 'MySQL', label: 'MySQL' },
        { value: 'SqlServer', label: 'SQL Server' }
      ]}
    />

    <div>
      <label class="block text-sm font-medium text-supabase-gray-700 mb-1">
        Connection String
        <span class="text-red-500">*</span>
      </label>
      <textarea
        bind:value={formData.originConStr}
        class="form-textarea"
        class:border-red-300={errors.originConStr}
        rows="3"
        placeholder="Server=localhost;Database=mydb;User Id=user;Password=password;"
        required
      ></textarea>
      {#if errors.originConStr}
        <p class="mt-1 text-sm text-red-600">{errors.originConStr}</p>
      {/if}
    </div>

    <Input
      label="Timezone Offset"
      type="number"
      bind:value={formData.originTimeZoneOffSet}
      placeholder="0"
      help="Hours offset from UTC (e.g., -5 for EST, +2 for CEST)"
    />

    <div class="flex justify-end space-x-3 pt-4">
      <Button variant="secondary" onclick={() => showModal = false}>
        Cancel
      </Button>
      <Button variant="primary" type="submit" loading={saving}>
        {modalMode === 'create' ? 'Create' : 'Save'} Origin
      </Button>
    </div>
  </form>
</Modal>