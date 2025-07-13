<script lang="ts">
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import type { Destination } from "$lib/types.js"
  import PageHeader from "$lib/components/layout/PageHeader.svelte"
  import Table from "$lib/components/ui/Table.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Input from "$lib/components/ui/Input.svelte"
  import Modal from "$lib/components/ui/Modal.svelte"
  import Select from "$lib/components/ui/Select.svelte"
  import Toast from "$lib/components/ui/Toast.svelte"
  import { Plus, Edit, Trash2, Upload } from "@lucide/svelte"

  let destinations = $state<Destination[]>([])
  let loading = $state(true)
  let searchTerm = $state("")
  let showModal = $state(false)
  let modalMode = $state<"create" | "edit">("create")
  let selectedDestination = $state<Destination | null>(null)
  let saving = $state(false)

  let currentPage = $state(1)
  let totalPages = $state(1)
  let totalItems = $state(0)
  let pageSize = $state(20)
  let sortKey = $state("")
  let sortDirection = $state<"asc" | "desc">("asc")

  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  let formData = $state({
    destinationName: "",
    destinationDbType: "",
    destinationConStr: "",
    destinationTimeZoneOffSet: 0,
  })

  let errors = $state<Record<string, string>>({})

  const columns = [
    { key: "id", label: "ID", sortable: true, width: "80px" },
    { key: "destinationName", label: "Name", sortable: true },
    {
      key: "destinationDbType",
      label: "Database Type",
      sortable: true,
      render: (value: string) => {
        const colors = {
          PostgreSQL: "bg-blue-100 text-blue-800",
          MySQL: "bg-orange-100 text-orange-800",
          SqlServer: "bg-green-100 text-green-800",
        }
        const colorClass =
          colors[value as keyof typeof colors] || "bg-gray-100 text-gray-800"
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colorClass}">${value}</span>`
      },
    },
    {
      key: "destinationTimeZoneOffSet",
      label: "Timezone Offset",
      sortable: true,
      render: (value: number) => `${value > 0 ? "+" : ""}${value}`,
    },
    {
      key: "actions",
      label: "Actions",
      render: (value: any, row: Destination) => {
        return `
          <div class="flex space-x-2">
            <button onclick="editDestination(${row.id})" class="text-green-600 hover:text-green-800" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteDestination(${row.id})" class="text-red-600 hover:text-red-800" title="Delete">
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
    setTimeout(() => (showToast = false), 5000)
  }

  onMount(async () => {
    await loadDestinations()
  })

  async function loadDestinations() {
    try {
      loading = true
      const filters: Record<string, string> = {
        take: pageSize.toString(),
        skip: ((currentPage - 1) * pageSize).toString(),
      }

      if (searchTerm) filters.name = searchTerm
      if (sortKey) {
        filters.sortBy = sortKey
        filters.sortDirection = sortDirection
      }

      const response = await api.getDestinations(filters)
      destinations = response.content || []

      // Calculate pagination
      totalItems = response.entityCount || 0
      totalPages = Math.ceil(totalItems / pageSize)
    } catch (error) {
      console.error("Failed to load destinations:", error)
      showToastMessage(
        "Failed to load destinations. Please check your connection and try again.",
        "error",
      )
    } finally {
      loading = false
    }
  }

  function openCreateModal() {
    modalMode = "create"
    selectedDestination = null
    formData = {
      destinationName: "",
      destinationDbType: "",
      destinationConStr: "",
      destinationTimeZoneOffSet: 0,
    }
    errors = {}
    showModal = true
  }

  function openEditModal(destination: Destination) {
    modalMode = "edit"
    selectedDestination = destination
    formData = {
      destinationName: destination.destinationName,
      destinationDbType: destination.destinationDbType,
      destinationConStr: "••••••••", // Don't show actual connection string
      destinationTimeZoneOffSet: destination.destinationTimeZoneOffSet,
    }
    errors = {}
    showModal = true
  }

  function validateForm(): boolean {
    errors = {}

    if (!formData.destinationName.trim()) {
      errors.destinationName = "Name is required"
    }

    if (!formData.destinationDbType) {
      errors.destinationDbType = "Database type is required"
    }

    if (!formData.destinationConStr.trim()) {
      errors.destinationConStr = "Connection string is required"
    }

    return Object.keys(errors).length === 0
  }

  async function handleSubmit() {
    if (!validateForm()) return

    try {
      saving = true

      const destinationData = {
        destinationName: formData.destinationName,
        destinationDbType: formData.destinationDbType,
        destinationConStr: formData.destinationConStr,
        destinationTimeZoneOffSet: formData.destinationTimeZoneOffSet,
      }

      if (modalMode === "create") {
        await api.createDestination(destinationData)
        showToastMessage("Destination created successfully", "success")
      } else if (selectedDestination) {
        await api.updateDestination(selectedDestination.id, destinationData)
        showToastMessage("Destination updated successfully", "success")
      }

      showModal = false
      await loadDestinations()
    } catch (error) {
      console.error(`Failed to ${modalMode} destination:`, error)
      showToastMessage(
        `Failed to ${modalMode} destination: ${error.message}`,
        "error",
      )
    } finally {
      saving = false
    }
  }

  function handlePageChange(page: number) {
    currentPage = page
    loadDestinations()
  }

  function handlePageSizeChange(newPageSize: number) {
    pageSize = newPageSize
    currentPage = 1
    loadDestinations()
  }

  function handleSort(key: string, direction: "asc" | "desc") {
    sortKey = key
    sortDirection = direction
    currentPage = 1
    loadDestinations()
  }

  // Global functions for table actions
  if (typeof window !== "undefined") {
    ;(window as any).editDestination = (id: number) => {
      const destination = destinations.find((d) => d.id === id)
      if (destination) openEditModal(destination)
    }
    ;(window as any).deleteDestination = async (id: number) => {
      if (confirm("Are you sure you want to delete this destination?")) {
        try {
          await api.deleteDestination(id)
          await loadDestinations()
          showToastMessage("Destination deleted successfully", "success")
        } catch (error) {
          console.error("Failed to delete destination:", error)
          showToastMessage("Failed to delete destination", "error")
        }
      }
    }
  }

  // Auto-reload when search term changes
  $effect(() => {
    currentPage = 1
    loadDestinations()
  })
</script>

<svelte:head>
  <title>Destinations - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader
    title="Destinations"
    description="Manage data destination connections"
  >
    {#snippet actions()}
      <Button variant="primary" onclick={openCreateModal}>
        <Plus size={16} class="mr-2" />
        New Destination
      </Button>
    {/snippet}
  </PageHeader>

  <!-- Filters -->
  <div class="bg-white p-4 rounded-lg shadow">
    <div class="max-w-md">
      <Input placeholder="Search destinations..." bind:value={searchTerm} />
    </div>
  </div>

  <!-- Destinations Table -->
  <div class="bg-white shadow rounded-lg">
    <div class="p-6">
      <Table
        {columns}
        data={destinations}
        {loading}
        emptyMessage="No destinations found"
        onSort={handleSort}
        {sortKey}
        {sortDirection}
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

<!-- Create/Edit Modal -->
<Modal
  bind:open={showModal}
  title={modalMode === "create" ? "New Destination" : "Edit Destination"}
>
  <!-- Fixed: Changed from on:submit to onsubmit -->
  <form onsubmit={handleSubmit} class="space-y-4">
    <Input
      label="Name"
      bind:value={formData.destinationName}
      error={errors.destinationName}
      required
      placeholder="Enter destination name"
    />

    <Select
      label="Database Type"
      bind:value={formData.destinationDbType}
      error={errors.destinationDbType}
      required
      placeholder="Select database type"
      options={[
        { value: "PostgreSQL", label: "PostgreSQL" },
        { value: "MySQL", label: "MySQL" },
        { value: "SqlServer", label: "SQL Server" },
      ]}
    />

    <!-- Fixed: Added proper id and for attributes for form label -->
    <div>
      <label
        for="destinationConnectionString"
        class="block text-sm font-medium text-supabase-gray-700 mb-1"
      >
        Connection String
        <span class="text-red-500">*</span>
      </label>
      <textarea
        id="destinationConnectionString"
        bind:value={formData.destinationConStr}
        class="form-textarea"
        class:border-red-300={errors.destinationConStr}
        rows="3"
        placeholder="Server=localhost;Database=mydb;User Id=user;Password=password;"
        required
      ></textarea>
      {#if errors.destinationConStr}
        <p class="mt-1 text-sm text-red-600">{errors.destinationConStr}</p>
      {/if}
    </div>

    <Input
      label="Timezone Offset"
      type="number"
      bind:value={formData.destinationTimeZoneOffSet}
      placeholder="0"
      help="Hours offset from UTC (e.g., -5 for EST, +2 for CEST)"
    />

    <div class="flex justify-end space-x-3 pt-4">
      <Button variant="secondary" onclick={() => (showModal = false)}>
        Cancel
      </Button>
      <Button variant="primary" type="submit" loading={saving}>
        {modalMode === "create" ? "Create" : "Save"} Destination
      </Button>
    </div>
  </form>
</Modal>

<!-- Toast Notifications -->
<Toast bind:show={showToast} type={toastType} message={toastMessage} />
