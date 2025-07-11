<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '$lib/api.js';
  import type { User } from '$lib/types.js';
  import PageHeader from '$lib/components/layout/PageHeader.svelte';
  import Table from '$lib/components/ui/Table.svelte';
  import Button from '$lib/components/ui/Button.svelte';
  import Input from '$lib/components/ui/Input.svelte';
  import Modal from '$lib/components/ui/Modal.svelte';
  import { Plus, Edit, Trash2, Users } from '@lucide/svelte';

  let users = $state<User[]>([]);
  let loading = $state(true);
  let searchTerm = $state('');
  let showModal = $state(false);
  let modalMode = $state<'create' | 'edit'>('create');
  let selectedUser = $state<User | null>(null);
  let saving = $state(false);
  let currentPage = $state(1);
  let totalPages = $state(1);
  const pageSize = 20;

  // Form data
  let formData = $state({
    username: '',
    password: '',
    confirmPassword: ''
  });

  let errors = $state<Record<string, string>>({});

  const columns = [
    { key: 'id', label: 'ID', sortable: true, width: '80px' },
    { key: 'username', label: 'Username', sortable: true },
    {
      key: 'actions',
      label: 'Actions',
      render: (value: any, row: User) => {
        return `
          <div class="flex space-x-2">
            <button onclick="editUser(${row.id})" class="text-green-600 hover:text-green-800" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteUser(${row.id})" class="text-red-600 hover:text-red-800" title="Delete">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
            </button>
          </div>
        `;
      }
    }
  ];

  onMount(async () => {
    await loadUsers();
  });

  async function loadUsers() {
    try {
      loading = true;
      const filters: Record<string, string> = {
        take: pageSize.toString()
      };
      if (searchTerm) filters.name = searchTerm;

      const response = await api.getUsers(filters);
      users = response.content || [];
      
      // Calculate pagination
      totalPages = Math.ceil((response.entityCount || users.length) / pageSize);
    } catch (error) {
      console.error('Failed to load users:', error);
      alert('Failed to load users. Please check your connection and try again.');
    } finally {
      loading = false;
    }
  }

  function handlePageChange(page: number) {
    currentPage = page;
    loadUsers();
  }

  function openCreateModal() {
    modalMode = 'create';
    selectedUser = null;
    formData = {
      username: '',
      password: '',
      confirmPassword: ''
    };
    errors = {};
    showModal = true;
  }

  function openEditModal(user: User) {
    modalMode = 'edit';
    selectedUser = user;
    formData = {
      username: user.username,
      password: '',
      confirmPassword: ''
    };
    errors = {};
    showModal = true;
  }

  function validateForm(): boolean {
    errors = {};

    if (!formData.username.trim()) {
      errors.username = 'Username is required';
    }

    if (modalMode === 'create' && !formData.password) {
      errors.password = 'Password is required';
    }

    if (formData.password && formData.password !== formData.confirmPassword) {
      errors.confirmPassword = 'Passwords do not match';
    }

    if (formData.password && formData.password.length < 6) {
      errors.password = 'Password must be at least 6 characters';
    }

    return Object.keys(errors).length === 0;
  }

  async function handleSubmit() {
    if (!validateForm()) return;

    try {
      saving = true;
      
      const userData = {
        username: formData.username,
        password: formData.password || undefined
      };

      if (modalMode === 'create') {
        await api.createUser(userData);
      } else if (selectedUser) {
        await api.updateUser(selectedUser.id, userData);
      }

      showModal = false;
      await loadUsers();
    } catch (error) {
      console.error(`Failed to ${modalMode} user:`, error);
      alert(`Failed to ${modalMode} user: ${error.message}`);
    } finally {
      saving = false;
    }
  }

  // Global functions for table actions
  if (typeof window !== 'undefined') {
    (window as any).editUser = (id: number) => {
      const user = users.find(u => u.id === id);
      if (user) openEditModal(user);
    };
    
    (window as any).deleteUser = async (id: number) => {
      if (confirm('Are you sure you want to delete this user?')) {
        try {
          await api.deleteUser(id);
          await loadUsers();
        } catch (error) {
          console.error('Failed to delete user:', error);
          alert('Failed to delete user');
        }
      }
    };
  }

  $effect(() => {
    loadUsers();
  });
</script>

<svelte:head>
  <title>Users - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader 
    title="Users" 
    description="Manage system users and access"
  >
    {#snippet actions()}
      <Button variant="primary" onclick={openCreateModal}>
        <Plus size={16} class="mr-2" />
        New User
      </Button>
    {/snippet}
  </PageHeader>

  <!-- Filters -->
  <div class="bg-white p-4 rounded-lg shadow">
    <div class="flex justify-between items-center gap-4">
      <div class="max-w-md flex-1">
        <Input
          placeholder="Search users..."
          bind:value={searchTerm}
        />
      </div>
      <div class="text-sm text-supabase-gray-600">
        Showing {users.length} users (Page {currentPage} of {totalPages})
      </div>
    </div>
  </div>

  <!-- Users Table -->
  <div class="bg-white shadow rounded-lg">
    <div class="p-6">
      <Table
        {columns}
        data={users}
        {loading}
        emptyMessage="No users found"
        pagination={{
          currentPage,
          totalPages,
          pageSize,
          totalItems: users.length,
          onPageChange: handlePageChange
        }}
      />
    </div>
  </div>
</div>

<!-- Create/Edit Modal -->
<Modal bind:open={showModal} title={modalMode === 'create' ? 'New User' : 'Edit User'}>
  <form onsubmit={handleSubmit} class="space-y-4">
    <Input
      label="Username"
      bind:value={formData.username}
      error={errors.username}
      required
      placeholder="Enter username"
    />

    <Input
      label={modalMode === 'create' ? 'Password' : 'New Password'}
      type="password"
      bind:value={formData.password}
      error={errors.password}
      required={modalMode === 'create'}
      placeholder={modalMode === 'create' ? 'Enter password' : 'Leave blank to keep current password'}
    />

    <Input
      label="Confirm Password"
      type="password"
      bind:value={formData.confirmPassword}
      error={errors.confirmPassword}
      required={modalMode === 'create' || !!formData.password}
      placeholder="Confirm password"
    />

    <div class="flex justify-end space-x-3 pt-4">
      <Button variant="secondary" onclick={() => showModal = false}>
        Cancel
      </Button>
      <Button variant="primary" type="submit" loading={saving}>
        {modalMode === 'create' ? 'Create' : 'Save'} User
      </Button>
    </div>
  </form>
</Modal>