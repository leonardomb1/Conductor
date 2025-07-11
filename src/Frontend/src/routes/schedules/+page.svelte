<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api.js';
  import type { Schedule } from '$lib/types.js';
  import PageHeader from '$lib/components/layout/PageHeader.svelte';
  import Table from '$lib/components/ui/Table.svelte';
  import Button from '$lib/components/ui/Button.svelte';
  import Input from '$lib/components/ui/Input.svelte';
  import Modal from '$lib/components/ui/Modal.svelte';
  import Select from '$lib/components/ui/Select.svelte';
  import Badge from '$lib/components/ui/Badge.svelte';
  import { Plus, Edit, Trash2, Calendar } from '@lucide/svelte';

  let schedules = $state<Schedule[]>([]);
  let loading = $state(true);
  let searchTerm = $state('');
  let showModal = $state(false);
  let modalMode = $state<'create' | 'edit'>('create');
  let selectedSchedule = $state<Schedule | null>(null);
  let saving = $state(false);
  let currentPage = $state(1);
  let totalPages = $state(1);
  const pageSize = 20;

  // Form data
  let formData = $state({
    scheduleName: '',
    status: true,
    value: 0
  });

  let errors = $state<Record<string, string>>({});

  const columns = [
    { key: 'id', label: 'ID', sortable: true, width: '80px' },
    { key: 'scheduleName', label: 'Name', sortable: true },
    { 
      key: 'status', 
      label: 'Status',
      render: (value: boolean) => {
        const variant = value ? 'success' : 'error';
        const text = value ? 'Active' : 'Inactive';
        const colors = {
          success: 'bg-green-100 text-green-800',
          error: 'bg-red-100 text-red-800'
        };
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colors[variant]}">${text}</span>`;
      }
    },
    { key: 'value', label: 'Value', sortable: true },
    {
      key: 'actions',
      label: 'Actions',
      render: (value: any, row: Schedule) => {
        return `
          <div class="flex space-x-2">
            <button onclick="editSchedule(${row.id})" class="text-green-600 hover:text-green-800" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteSchedule(${row.id})" class="text-red-600 hover:text-red-800" title="Delete">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
            </button>
          </div>
        `;
      }
    }
  ];

  onMount(async () => {
    await loadSchedules();
  });

  async function loadSchedules() {
    try {
      loading = true;
      const filters: Record<string, string> = {
        take: pageSize.toString()
      };
      
      if (searchTerm) filters.name = searchTerm;

      const response = await api.getSchedules(filters);
      schedules = response.content || [];
      
      // Calculate pagination (mock for now, backend doesn't return total count)
      totalPages = Math.ceil((response.entityCount || schedules.length) / pageSize);
    } catch (error) {
      console.error('Failed to load schedules:', error);
    } finally {
      loading = false;
    }
  }

  function openCreateModal() {
    modalMode = 'create';
    selectedSchedule = null;
    formData = {
      scheduleName: '',
      status: true,
      value: 0
    };
    errors = {};
    showModal = true;
  }

  function openEditModal(schedule: Schedule) {
    modalMode = 'edit';
    selectedSchedule = schedule;
    formData = {
      scheduleName: schedule.scheduleName,
      status: schedule.status,
      value: schedule.value
    };
    errors = {};
    showModal = true;
  }

  function validateForm(): boolean {
    errors = {};

    if (!formData.scheduleName.trim()) {
      errors.scheduleName = 'Name is required';
    }

    if (formData.value < 0) {
      errors.value = 'Value must be non-negative';
    }

    return Object.keys(errors).length === 0;
  }

  async function handleSubmit() {
    if (!validateForm()) return;

    try {
      saving = true;
      
      const scheduleData = {
        scheduleName: formData.scheduleName,
        status: formData.status,
        value: formData.value
      };

      if (modalMode === 'create') {
        await api.createSchedule(scheduleData);
      } else if (selectedSchedule) {
        await api.updateSchedule(selectedSchedule.id, scheduleData);
      }

      showModal = false;
      await loadSchedules();
    } catch (error) {
      console.error(`Failed to ${modalMode} schedule:`, error);
      alert(`Failed to ${modalMode} schedule`);
    } finally {
      saving = false;
    }
  }

  // Global functions for table actions
  if (typeof window !== 'undefined') {
    (window as any).editSchedule = (id: number) => {
      const schedule = schedules.find(s => s.id === id);
      if (schedule) openEditModal(schedule);
    };
    
    (window as any).deleteSchedule = async (id: number) => {
      if (confirm('Are you sure you want to delete this schedule?')) {
        try {
          await api.deleteSchedule(id);
          await loadSchedules();
        } catch (error) {
          console.error('Failed to delete schedule:', error);
          alert('Failed to delete schedule');
        }
      }
    };
  }

  $effect(() => {
    loadSchedules();
  });
</script>

<svelte:head>
  <title>Schedules - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader 
    title="Schedules" 
    description="Manage extraction schedules and timing configurations"
  >
    {#snippet actions()}
      <Button variant="primary" onclick={openCreateModal}>
        <Plus size={16} class="mr-2" />
        New Schedule
      </Button>
    {/snippet}
  </PageHeader>

  <!-- Filters -->
  <div class="bg-white p-4 rounded-lg shadow">
    <div class="flex justify-between items-center gap-4">
      <div class="max-w-md flex-1">
        <Input
          placeholder="Search schedules..."
          bind:value={searchTerm}
        />
      </div>
      <div class="text-sm text-supabase-gray-600">
        Showing {schedules.length} of {schedules.length} schedules
      </div>
    </div>
  </div>

  <!-- Schedules Table -->
  <div class="bg-white shadow rounded-lg">
    <div class="p-6">
      <Table
        {columns}
        data={schedules}
        {loading}
        emptyMessage="No schedules found"
      />
    </div>
  </div>
</div>

<!-- Create/Edit Modal -->
<Modal bind:open={showModal} title={modalMode === 'create' ? 'New Schedule' : 'Edit Schedule'}>
  <form onsubmit={handleSubmit} class="space-y-4">
    <Input
      label="Name"
      bind:value={formData.scheduleName}
      error={errors.scheduleName}
      required
      placeholder="Enter schedule name"
    />

    <div>
      <label class="flex items-center space-x-2">
        <input
          type="checkbox"
          bind:checked={formData.status}
          class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
        />
        <span class="text-sm font-medium text-supabase-gray-700">Active</span>
      </label>
    </div>

    <Input
      label="Value"
      type="number"
      bind:value={formData.value}
      error={errors.value}
      required
      placeholder="Enter schedule value"
      help="Scheduling interval value (implementation dependent)"
    />

    <div class="flex justify-end space-x-3 pt-4">
      <Button variant="secondary" onclick={() => showModal = false}>
        Cancel
      </Button>
      <Button variant="primary" type="submit" loading={saving}>
        {modalMode === 'create' ? 'Create' : 'Save'} Schedule
      </Button>
    </div>
  </form>
</Modal>