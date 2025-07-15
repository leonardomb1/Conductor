<script lang="ts">
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import type { User } from "$lib/types.js"
  import PageHeader from "$lib/components/layout/PageHeader.svelte"
  import Table from "$lib/components/ui/Table.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Input from "$lib/components/ui/Input.svelte"
  import Modal from "$lib/components/ui/Modal.svelte"
  import Toast from "$lib/components/ui/Toast.svelte"
  import ConfirmationModal from "$lib/components/ui/ConfirmationModal.svelte"
  import { Plus, Edit, Trash2, Users } from "@lucide/svelte"

  let users = $state<User[]>([])
  let loading = $state(true)
  let searchTerm = $state("")
  let showModal = $state(false)
  let modalMode = $state<"create" | "edit">("create")
  let selectedUser = $state<User | null>(null)
  let saving = $state(false)
  let currentPage = $state(1)
  let totalPages = $state(1)
  let totalItems = $state(0)
  let pageSize = $state(20)

  // Confirmation modal state
  let showConfirmModal = $state(false)
  let confirmAction = $state<() => Promise<void>>(() => Promise.resolve())
  let confirmMessage = $state("")
  let confirmTitle = $state("")
  let confirmLoading = $state(false)

  // Toast state
  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  // Form data
  let formData = $state({
    username: "",
    password: "",
    confirmPassword: "",
  })

  let errors = $state<Record<string, string>>({})

  // Mobile-optimized columns
  const mobileColumns = [
    { 
      key: "username", 
      label: "User", 
      render: (value: string, row: User) => {
        return `
          <div class="space-y-3">
            <div class="flex items-start justify-between">
              <div class="min-w-0 flex-1">
                <h3 class="font-medium text-gray-900 dark:text-white text-base leading-tight">${value}</h3>
              </div>
              <div class="flex items-center space-x-2 ml-3">
                <button onclick="editUser(${row.id})" class="p-2 text-green-600 hover:text-green-800 dark:text-green-400 dark:hover:text-green-300 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition-colors" title="Edit">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
                </button>
                <button onclick="deleteUser(${row.id})" class="p-2 text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition-colors" title="Delete">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
                </button>
              </div>
            </div>
            <div class="flex items-center space-x-3">
              <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200">
                User Account
              </span>
            </div>
            <div class="text-xs text-gray-500 dark:text-gray-400">
              <span class="font-medium">ID:</span> ${row.id}
            </div>
          </div>
        `
      }
    }
  ]

  // Desktop columns
  const columns = [
    { key: "id", label: "ID", sortable: true, width: "80px" },
    { key: "username", label: "Username", sortable: true },
    {
      key: "actions",
      label: "Actions",
      render: (value: any, row: User) => {
        return `
          <div class="flex space-x-2">
            <button onclick="editUser(${row.id})" class="text-green-600 hover:text-green-800 dark:text-green-400 dark:hover:text-green-300" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteUser(${row.id})" class="text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300" title="Delete">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
            </button>
          </div>
        `
      },
    },
  ]

  function showToastMessage(
    message: string,
    type: "success" | "error" | "info" = "info",
  ) {
    toastMessage = message
    toastType = type
    showToast = true
  }

  onMount(async () => {
    await loadUsers()
  })

  async function loadUsers() {
    try {
      loading = true
      const filters: Record<string, string> = {
        take: pageSize.toString(),
        skip: ((currentPage - 1) * pageSize).toString(),
      }
      if (searchTerm) filters.name = searchTerm

      const response = await api.getUsers(filters)
      users = response.content || []

      // Calculate pagination
      totalItems = response.entityCount || 0
      totalPages = Math.ceil(totalItems / pageSize)
    } catch (error) {
      showToastMessage(
        "Failed to load users. Please check your connection and try again.",
        "error",
      )
    } finally {
      loading = false
    }
  }

  function handlePageChange(page: number) {
    currentPage = page
    loadUsers()
  }

  function handlePageSizeChange(newPageSize: number) {
    pageSize = newPageSize
    currentPage = 1
    loadUsers()
  }

  function openCreateModal() {
    modalMode = "create"
    selectedUser = null
    formData = {
      username: "",
      password: "",
      confirmPassword: "",
    }
    errors = {}
    showModal = true
  }

  function openEditModal(user: User) {
    modalMode = "edit"
    selectedUser = user
    formData = {
      username: user.username,
      password: "",
      confirmPassword: "",
    }
    errors = {}
    showModal = true
  }

  function validateForm(): boolean {
    errors = {}

    if (!formData.username.trim()) {
      errors.username = "Username is required"
    }

    if (modalMode === "create" && !formData.password) {
      errors.password = "Password is required"
    }

    if (formData.password && formData.password !== formData.confirmPassword) {
      errors.confirmPassword = "Passwords do not match"
    }

    if (formData.password && formData.password.length < 6) {
      errors.password = "Password must be at least 6 characters"
    }

    return Object.keys(errors).length === 0
  }

  async function handleSubmit() {
    if (!validateForm()) return

    try {
      saving = true

      const userData = {
        username: formData.username,
        password: formData.password || undefined,
      }

      if (modalMode === "create") {
        await api.createUser(userData)
        showToastMessage("User created successfully", "success")
      } else if (selectedUser) {
        await api.updateUser(selectedUser.id, userData)
        showToastMessage("User updated successfully", "success")
      }

      showModal = false
      await loadUsers()
    } catch (error) {
      showToastMessage(`Failed to ${modalMode} user: ${error.message}`, "error")
    } finally {
      saving = false
    }
  }

  function showDeleteConfirmation(id: number) {
    const user = users.find((u) => u.id === id)
    confirmTitle = "Delete User"
    confirmMessage = `Are you sure you want to delete user "${user?.username || "this user"}"? This action cannot be undone.`
    confirmAction = async () => {
      confirmLoading = true
      try {
        await api.deleteUser(id)
        await loadUsers()
        showToastMessage("User deleted successfully", "success")
      } catch (error) {
        showToastMessage("Failed to delete user", "error")
        throw error
      } finally {
        confirmLoading = false
      }
    }
    showConfirmModal = true
  }

  // Global functions for table actions
  if (typeof window !== "undefined") {
    ;(window as any).editUser = (id: number) => {
      const user = users.find((u) => u.id === id)
      if (user) openEditModal(user)
    }

    ;(window as any).deleteUser = (id: number) => {
      showDeleteConfirmation(id)
    }
  }

  $effect(() => {
    loadUsers()
  })
</script>

<svelte:head>
  <title>Users - Conductor</title>
</svelte:head>

<div class="space-y-4 sm:space-y-6">
  <PageHeader title="Users" description="Manage system users and access">
    {#snippet actions()}
      <Button variant="primary" onclick={openCreateModal}>
        <Plus size={16} class="mr-2" />
        New User
      </Button>
    {/snippet}
  </PageHeader>

  <!-- Search -->
  <div class="bg-white dark:bg-gray-800 p-4 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700">
    <div class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-4">
      <div class="flex-1 max-w-md">
        <Input placeholder="Search users..." bind:value={searchTerm} />
      </div>
      <div class="text-sm text-gray-600 dark:text-gray-400">
        Showing {users.length} of {totalItems.toLocaleString()} users
      </div>
    </div>
  </div>

  <!-- Users Content -->
  <div class="bg-white dark:bg-gray-800 shadow-sm rounded-lg border border-gray-200 dark:border-gray-700">
    <div class="p-3 sm:p-6">
      <!-- Mobile view: Card layout -->
      <div class="block sm:hidden space-y-3">
        {#if loading}
          <div class="flex justify-center py-8">
            <svg class="animate-spin h-8 w-8 text-gray-500 dark:text-gray-400" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
            </svg>
          </div>
        {:else if users.length === 0}
          <div class="text-center py-8">
            <Users class="mx-auto h-12 w-12 text-gray-400 dark:text-gray-500" />
            <h3 class="mt-2 text-sm font-medium text-gray-900 dark:text-white">
              No users found
            </h3>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
              Get started by creating a new user.
            </p>
          </div>
        {:else}
          {#each users as user}
            <div class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 border border-gray-200 dark:border-gray-600">
              {@html mobileColumns[0].render(user.username, user)}
            </div>
          {/each}
        {/if}

        <!-- Mobile Pagination -->
        {#if totalPages > 1}
          <div class="flex justify-between items-center pt-4 border-t border-gray-200 dark:border-gray-700 gap-2">
            <Button
              variant="secondary"
              onclick={() => handlePageChange(currentPage - 1)}
              disabled={currentPage <= 1}
              class="min-h-[44px] px-4 py-2 text-sm font-medium"
            >
              Previous
            </Button>
            <span class="text-sm text-gray-600 dark:text-gray-400 font-medium px-2">
              {currentPage} / {totalPages}
            </span>
            <Button
              variant="secondary"
              onclick={() => handlePageChange(currentPage + 1)}
              disabled={currentPage >= totalPages}
              class="min-h-[44px] px-4 py-2 text-sm font-medium"
            >
              Next
            </Button>
          </div>
        {/if}
      </div>

      <!-- Desktop view: Table layout -->
      <div class="hidden sm:block">
        <Table
          {columns}
          data={users}
          {loading}
          emptyMessage="No users found"
          pagination={{
            currentPage,
            totalPages,
            pageSize,
            totalItems,
            onPageChange: handlePageChange,
            onPageSizeChange: handlePageSizeChange,
          }}
        />
      </div>
    </div>
  </div>
</div>

<!-- Create/Edit Modal -->
<Modal
  bind:open={showModal}
  title={modalMode === "create" ? "New User" : "Edit User"}
>
  <form onsubmit={handleSubmit} class="space-y-4">
    <Input
      label="Username"
      bind:value={formData.username}
      error={errors.username}
      required
      placeholder="Enter username"
    />

    <Input
      label={modalMode === "create" ? "Password" : "New Password"}
      type="password"
      bind:value={formData.password}
      error={errors.password}
      required={modalMode === "create"}
      placeholder={modalMode === "create"
        ? "Enter password"
        : "Leave blank to keep current password"}
    />

    <Input
      label="Confirm Password"
      type="password"
      bind:value={formData.confirmPassword}
      error={errors.confirmPassword}
      required={modalMode === "create" || !!formData.password}
      placeholder="Confirm password"
    />

    <div class="flex flex-col sm:flex-row justify-end space-y-3 sm:space-y-0 sm:space-x-3 pt-4">
      <Button variant="secondary" onclick={() => (showModal = false)}>
        Cancel
      </Button>
      <Button variant="primary" type="submit" loading={saving}>
        {modalMode === "create" ? "Create" : "Save"} User
      </Button>
    </div>
  </form>
</Modal>

<!-- Confirmation Modal -->
<ConfirmationModal
  bind:open={showConfirmModal}
  title={confirmTitle}
  message={confirmMessage}
  type="danger"
  loading={confirmLoading}
  onConfirm={confirmAction}
/>

<!-- Toast Notifications -->
<Toast bind:show={showToast} type={toastType} message={toastMessage} />