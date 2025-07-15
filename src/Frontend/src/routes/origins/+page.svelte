<script lang="ts">
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import type { Origin } from "$lib/types.js"
  import PageHeader from "$lib/components/layout/PageHeader.svelte"
  import Table from "$lib/components/ui/Table.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Input from "$lib/components/ui/Input.svelte"
  import Modal from "$lib/components/ui/Modal.svelte"
  import Select from "$lib/components/ui/Select.svelte"
  import Toast from "$lib/components/ui/Toast.svelte"
  import ConfirmationModal from "$lib/components/ui/ConfirmationModal.svelte"
  import { Plus, Edit, Trash2, Database } from "@lucide/svelte"

  let origins = $state<Origin[]>([])
  let loading = $state(true)
  let searchTerm = $state("")
  let showModal = $state(false)
  let modalMode = $state<"create" | "edit">("create")
  let selectedOrigin = $state<Origin | null>(null)
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
    originName: "",
    originAlias: "",
    originDbType: "",
    originConStr: "",
    originTimeZoneOffSet: 0,
  })

  let errors = $state<Record<string, string>>({})

  // Mobile-optimized columns
  const mobileColumns = [
    { 
      key: "originName", 
      label: "Origin", 
      render: (value: string, row: Origin) => {
        const dbTypeColors = {
          PostgreSQL: "bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300",
          MySQL: "bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-300",
          SqlServer: "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300",
        }
        const dbTypeColor = dbTypeColors[row.originDbType as keyof typeof dbTypeColors] || "bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200"
        
        return `
          <div class="space-y-3">
            <div class="flex items-start justify-between">
              <div class="min-w-0 flex-1">
                <h3 class="font-medium text-gray-900 dark:text-white text-base leading-tight">${value}</h3>
                ${row.originAlias ? `<p class="text-sm text-gray-600 dark:text-gray-400 mt-1">${row.originAlias}</p>` : ''}
              </div>
              <div class="flex items-center space-x-2 ml-3">
                <button onclick="editOrigin(${row.id})" class="p-2 text-green-600 hover:text-green-800 dark:text-green-400 dark:hover:text-green-300 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition-colors" title="Edit">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
                </button>
                <button onclick="deleteOrigin(${row.id})" class="p-2 text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition-colors" title="Delete">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
                </button>
              </div>
            </div>
            <div class="flex items-center space-x-3">
              ${row.originDbType ? `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${dbTypeColor}">${row.originDbType}</span>` : ''}
              <span class="text-xs text-gray-500 dark:text-gray-400">
                <span class="font-medium">Timezone:</span> ${row.originTimeZoneOffSet ? (row.originTimeZoneOffSet > 0 ? '+' : '') + row.originTimeZoneOffSet : '0'}
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
    { key: "originName", label: "Name", sortable: true },
    {
      key: "originAlias",
      label: "Alias",
      render: (value: string) => value || "-",
    },
    {
      key: "originDbType",
      label: "Database Type",
      render: (value: string) => {
        if (!value) return "-"
        const colors = {
          PostgreSQL: "bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300",
          MySQL: "bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-300",
          SqlServer: "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300",
        }
        const colorClass =
          colors[value as keyof typeof colors] || "bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200"
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colorClass}">${value}</span>`
      },
    },
    {
      key: "originTimeZoneOffSet",
      label: "Timezone Offset",
      render: (value: number) =>
        value ? `${value > 0 ? "+" : ""}${value}` : "0",
    },
    {
      key: "actions",
      label: "Actions",
      render: (value: any, row: Origin) => {
        return `
          <div class="flex space-x-2">
            <button onclick="editOrigin(${row.id})" class="text-green-600 hover:text-green-800 dark:text-green-400 dark:hover:text-green-300" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteOrigin(${row.id})" class="text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300" title="Delete">
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
    await loadOrigins()
  })

  async function loadOrigins() {
    try {
      loading = true
      const filters: Record<string, string> = {
        take: pageSize.toString(),
        skip: ((currentPage - 1) * pageSize).toString(),
      }
      if (searchTerm) filters.name = searchTerm

      const response = await api.getOrigins(filters)
      origins = response.content || []

      // Calculate pagination
      totalItems = response.entityCount || 0
      totalPages = Math.ceil(totalItems / pageSize)
    } catch (error) {
      showToastMessage(
        "Failed to load origins. Please check your connection and try again.",
        "error",
      )
    } finally {
      loading = false
    }
  }

  function openCreateModal() {
    modalMode = "create"
    selectedOrigin = null
    formData = {
      originName: "",
      originAlias: "",
      originDbType: "",
      originConStr: "",
      originTimeZoneOffSet: 0,
    }
    errors = {}
    showModal = true
  }

  function openEditModal(origin: Origin) {
    modalMode = "edit"
    selectedOrigin = origin
    formData = {
      originName: origin.originName,
      originAlias: origin.originAlias || "",
      originDbType: origin.originDbType || "",
      originConStr: "••••••••", // Don't show actual connection string
      originTimeZoneOffSet: origin.originTimeZoneOffSet || 0,
    }
    errors = {}
    showModal = true
  }

  function validateForm(): boolean {
    errors = {}

    if (!formData.originName.trim()) {
      errors.originName = "Name is required"
    }

    if (!formData.originDbType) {
      errors.originDbType = "Database type is required"
    }

    if (!formData.originConStr.trim()) {
      errors.originConStr = "Connection string is required"
    }

    return Object.keys(errors).length === 0
  }

  async function handleSubmit() {
    if (!validateForm()) return

    try {
      saving = true

      const originData = {
        originName: formData.originName,
        originAlias: formData.originAlias || undefined,
        originDbType: formData.originDbType,
        originConStr: formData.originConStr,
        originTimeZoneOffSet: formData.originTimeZoneOffSet,
      }

      if (modalMode === "create") {
        await api.createOrigin(originData)
        showToastMessage("Origin created successfully", "success")
      } else if (selectedOrigin) {
        await api.updateOrigin(selectedOrigin.id, originData)
        showToastMessage("Origin updated successfully", "success")
      }

      showModal = false
      await loadOrigins()
    } catch (error) {
      showToastMessage(
        `Failed to ${modalMode} origin: ${error.message}`,
        "error",
      )
    } finally {
      saving = false
    }
  }

  function handlePageChange(page: number) {
    currentPage = page
    loadOrigins()
  }

  function handlePageSizeChange(newPageSize: number) {
    pageSize = newPageSize
    currentPage = 1
    loadOrigins()
  }

  function showDeleteConfirmation(id: number) {
    const origin = origins.find((o) => o.id === id)
    confirmTitle = "Delete Origin"
    confirmMessage = `Are you sure you want to delete "${origin?.originName || "this origin"}"? This action cannot be undone.`
    confirmAction = async () => {
      confirmLoading = true
      try {
        await api.deleteOrigin(id)
        await loadOrigins()
        showToastMessage("Origin deleted successfully", "success")
      } catch (error) {
        showToastMessage("Failed to delete origin", "error")
        throw error
      } finally {
        confirmLoading = false
      }
    }
    showConfirmModal = true
  }

  // Global functions for table actions
  if (typeof window !== "undefined") {
    ;(window as any).editOrigin = (id: number) => {
      const origin = origins.find((o) => o.id === id)
      if (origin) openEditModal(origin)
    }
    ;(window as any).deleteOrigin = (id: number) => {
      showDeleteConfirmation(id)
    }
  }

  $effect(() => {
    loadOrigins()
  })
</script>

<svelte:head>
  <title>Origins - Conductor</title>
</svelte:head>

<div class="space-y-4 sm:space-y-6">
  <PageHeader title="Origins" description="Manage data source connections">
    {#snippet actions()}
      <Button variant="primary" onclick={openCreateModal}>
        <Plus size={16} class="mr-2" />
        New Origin
      </Button>
    {/snippet}
  </PageHeader>

  <!-- Search -->
  <div class="bg-white dark:bg-gray-800 p-4 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700">
    <div class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-4">
      <div class="flex-1 max-w-md">
        <Input placeholder="Search origins..." bind:value={searchTerm} />
      </div>
      <div class="text-sm text-gray-600 dark:text-gray-400">
        Showing {origins.length} of {totalItems.toLocaleString()} origins
      </div>
    </div>
  </div>

  <!-- Origins Content -->
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
        {:else if origins.length === 0}
          <div class="text-center py-8">
            <Database class="mx-auto h-12 w-12 text-gray-400 dark:text-gray-500" />
            <h3 class="mt-2 text-sm font-medium text-gray-900 dark:text-white">
              No origins found
            </h3>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
              Get started by creating a new origin.
            </p>
          </div>
        {:else}
          {#each origins as origin}
            <div class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 border border-gray-200 dark:border-gray-600">
              {@html mobileColumns[0].render(origin.originName, origin)}
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
          data={origins}
          {loading}
          emptyMessage="No origins found"
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
  title={modalMode === "create" ? "New Origin" : "Edit Origin"}
>
  <form onsubmit={handleSubmit} class="space-y-4">
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
        { value: "PostgreSQL", label: "PostgreSQL" },
        { value: "MySQL", label: "MySQL" },
        { value: "SqlServer", label: "SQL Server" },
      ]}
    />

    <div>
      <label
        for="connectionString"
        class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
      >
        Connection String
        <span class="text-red-500">*</span>
      </label>
      <textarea
        id="connectionString"
        bind:value={formData.originConStr}
        class="form-textarea"
        class:border-red-300={errors.originConStr}
        class:dark:border-red-500={errors.originConStr}
        rows="3"
        placeholder="Server=localhost;Database=mydb;User Id=user;Password=password;"
        required
      ></textarea>
      {#if errors.originConStr}
        <p class="mt-1 text-sm text-red-600 dark:text-red-400">{errors.originConStr}</p>
      {/if}
    </div>

    <Input
      label="Timezone Offset"
      type="number"
      bind:value={formData.originTimeZoneOffSet}
      placeholder="0"
      help="Hours offset from UTC (e.g., -5 for EST, +2 for CEST)"
    />

    <div class="flex flex-col sm:flex-row justify-end space-y-3 sm:space-y-0 sm:space-x-3 pt-4">
      <Button variant="secondary" onclick={() => (showModal = false)}>
        Cancel
      </Button>
      <Button variant="primary" type="submit" loading={saving}>
        {modalMode === "create" ? "Create" : "Save"} Origin
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