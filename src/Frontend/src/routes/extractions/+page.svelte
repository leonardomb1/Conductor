<script lang="ts">
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import { extractionsStore } from "$lib/stores/extractions.svelte.js"
  import type { Extraction } from "$lib/types.js"
  import PageHeader from "$lib/components/layout/PageHeader.svelte"
  import Table from "$lib/components/ui/Table.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Input from "$lib/components/ui/Input.svelte"
  import Select from "$lib/components/ui/Select.svelte"
  import Badge from "$lib/components/ui/Badge.svelte"
  import Modal from "$lib/components/ui/Modal.svelte"
  import Toast from "$lib/components/ui/Toast.svelte"
  import {
    Plus,
    Play,
    Download,
    Eye,
    Edit,
    Trash2,
    Search,
    X,
    RefreshCw,
  } from "@lucide/svelte"

  const allExtractions = $derived(extractionsStore.data)
  const loading = $derived(extractionsStore.loading)

  let searchTerm = $state("")
  let filterOrigin = $state("")
  let filterDestination = $state("")
  let filterSchedule = $state("")
  let filterType = $state("")
  let filterIncremental = $state("")

  let showExecuteModal = $state(false)
  let executeType = $state<"transfer" | "pull">("transfer")
  let executeLoading = $state(false)
  let selectedExtractions = $state<number[]>([])

  let currentPage = $state(1)
  let pageSize = $state(50)
  let sortKey = $state("")
  let sortDirection = $state<"asc" | "desc">("asc")

  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  const filteredExtractions = $derived(() => {
    let filtered = [...allExtractions]

    if (searchTerm.trim()) {
      const searchLower = searchTerm.toLowerCase()
      filtered = filtered.filter(
        (e) =>
          e.extractionName?.toLowerCase().includes(searchLower) ||
          e.extractionAlias?.toLowerCase().includes(searchLower) ||
          e.indexName?.toLowerCase().includes(searchLower),
      )
    }

    if (filterOrigin.trim()) {
      const originLower = filterOrigin.toLowerCase()
      filtered = filtered.filter((e) =>
        e.origin?.originName?.toLowerCase().includes(originLower),
      )
    }

    if (filterDestination.trim()) {
      const destLower = filterDestination.toLowerCase()
      filtered = filtered.filter((e) =>
        e.destination?.destinationName?.toLowerCase().includes(destLower),
      )
    }

    if (filterSchedule.trim()) {
      const scheduleLower = filterSchedule.toLowerCase()
      filtered = filtered.filter((e) =>
        e.schedule?.scheduleName?.toLowerCase().includes(scheduleLower),
      )
    }

    if (filterType) {
      filtered = filtered.filter((e) => (e.sourceType || "db") === filterType)
    }

    if (filterIncremental) {
      const isIncremental = filterIncremental === "true"
      filtered = filtered.filter((e) => e.isIncremental === isIncremental)
    }

    if (sortKey) {
      filtered.sort((a, b) => {
        let aVal: any, bVal: any

        switch (sortKey) {
          case "origin":
            aVal = a.origin?.originName || ""
            bVal = b.origin?.originName || ""
            break
          case "destination":
            aVal = a.destination?.destinationName || ""
            bVal = b.destination?.destinationName || ""
            break
          case "schedule":
            aVal = a.schedule?.scheduleName || ""
            bVal = b.schedule?.scheduleName || ""
            break
          default:
            aVal = a[sortKey as keyof Extraction] || ""
            bVal = b[sortKey as keyof Extraction] || ""
        }

        if (typeof aVal === "string" && typeof bVal === "string") {
          aVal = aVal.toLowerCase()
          bVal = bVal.toLowerCase()
        }

        if (aVal < bVal) return sortDirection === "asc" ? -1 : 1
        if (aVal > bVal) return sortDirection === "asc" ? 1 : -1
        return 0
      })
    }

    return filtered
  })

  const paginatedExtractions = $derived(() => {
    const startIndex = (currentPage - 1) * pageSize
    const endIndex = startIndex + pageSize
    return filteredExtractions.slice(startIndex, endIndex)
  })

  const totalItems = $derived(filteredExtractions.length)
  const totalPages = $derived(Math.ceil(totalItems / pageSize))

  const cacheInfo = $derived(() => ({
    lastLoaded: extractionsStore.lastLoaded,
    isStale: extractionsStore.isStale,
    error: extractionsStore.error,
  }))

  const columns = [
    {
      key: "selection",
      label: "",
      width: "40px",
      render: (value: any, row: Extraction) => {
        const isSelected = selectedExtractions.includes(row.id)
        return `
          <input 
            type="checkbox" 
            ${isSelected ? "checked" : ""} 
            onchange="toggleSelection(${row.id})"
            class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
          />
        `
      },
    },
    { key: "id", label: "ID", sortable: true, width: "80px" },
    { key: "extractionName", label: "Name", sortable: true },
    {
      key: "sourceType",
      label: "Type",
      sortable: true,
      render: (value: string) => {
        const type = value || "db"
        const variant =
          type === "http" ? "info" : type === "db" ? "success" : "default"
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-${variant === "info" ? "blue" : variant === "success" ? "green" : "gray"}-100 text-${variant === "info" ? "blue" : variant === "success" ? "green" : "gray"}-800">${type}</span>`
      },
    },
    {
      key: "origin",
      label: "Origin",
      sortable: true,
      render: (value: any) => value?.originName || "-",
    },
    {
      key: "destination",
      label: "Destination",
      sortable: true,
      render: (value: any) => value?.destinationName || "-",
    },
    {
      key: "schedule",
      label: "Schedule",
      sortable: true,
      render: (value: any) => value?.scheduleName || "-",
    },
    {
      key: "isIncremental",
      label: "Incremental",
      sortable: true,
      render: (value: boolean) => {
        return value
          ? '<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">Yes</span>'
          : '<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">No</span>'
      },
    },
    {
      key: "actions",
      label: "Actions",
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
    await extractionsStore.loadExtractions()
  })

  async function refreshData() {
    const success = await extractionsStore.refreshExtractions()
    if (success) {
      showToastMessage("Data refreshed successfully", "success")
    } else {
      showToastMessage("Failed to refresh data", "error")
    }
  }

  function clearFilters() {
    searchTerm = ""
    filterOrigin = ""
    filterDestination = ""
    filterSchedule = ""
    filterType = ""
    filterIncremental = ""
    currentPage = 1
  }

  async function executeExtractions() {
    if (selectedExtractions.length === 0) {
      showToastMessage("Please select at least one extraction", "error")
      return
    }

    executeLoading = true
    try {
      const selectedNames = selectedExtractions
        .map((id) => allExtractions.find((e) => e.id === id)?.extractionName)
        .filter(Boolean)

      if (selectedNames.length === 0) {
        showToastMessage("No valid extractions selected", "error")
        return
      }

      const filters = {
        contains: selectedNames.join(","),
      }

      if (executeType === "transfer") {
        await api.executeTransfer(filters)
        showToastMessage(
          `Transfer job started successfully for ${selectedNames.length} extraction${selectedNames.length > 1 ? "s" : ""}`,
          "success",
        )
      } else {
        await api.executePull(filters)
        showToastMessage(
          `Pull job started successfully for ${selectedNames.length} extraction${selectedNames.length > 1 ? "s" : ""}`,
          "success",
        )
      }

      showExecuteModal = false
      selectedExtractions = []
    } catch (error) {
      console.error(`Failed to execute ${executeType}:`, error)
      showToastMessage(
        `Failed to start ${executeType} job: ${error.message}`,
        "error",
      )
    } finally {
      executeLoading = false
    }
  }

  function toggleExtractionSelection(id: number) {
    if (selectedExtractions.includes(id)) {
      selectedExtractions = selectedExtractions.filter((eid) => eid !== id)
    } else {
      selectedExtractions = [...selectedExtractions, id]
    }
  }

  function selectAllVisible() {
    const visibleIds = paginatedExtractions.map((e) => e.id)
    selectedExtractions = [...new Set([...selectedExtractions, ...visibleIds])]
  }

  function deselectAllVisible() {
    const visibleIds = paginatedExtractions.map((e) => e.id)
    selectedExtractions = selectedExtractions.filter(
      (id) => !visibleIds.includes(id),
    )
  }

  function selectAll() {
    selectedExtractions = filteredExtractions.map((e) => e.id)
  }

  function deselectAll() {
    selectedExtractions = []
  }

  function handlePageChange(page: number) {
    currentPage = page
    // Clear selections that are no longer visible
    const visibleIds = paginatedExtractions.map((e) => e.id)
    selectedExtractions = selectedExtractions.filter((id) =>
      visibleIds.includes(id),
    )
  }

  function handlePageSizeChange(newPageSize: number) {
    pageSize = newPageSize
    currentPage = 1
  }

  function handleSort(key: string, direction: "asc" | "desc") {
    sortKey = key
    sortDirection = direction
    currentPage = 1
  }

  // Reset to first page when filters change
  $effect(() => {
    if (
      searchTerm ||
      filterOrigin ||
      filterDestination ||
      filterSchedule ||
      filterType ||
      filterIncremental
    ) {
      currentPage = 1
    }
  })

  // Global functions for table actions
  if (typeof window !== "undefined") {
    ;(window as any).viewExtraction = (id: number) => {
      window.location.href = `/extractions/${id}`
    }
    ;(window as any).editExtraction = (id: number) => {
      window.location.href = `/extractions/${id}/edit`
    }
    ;(window as any).deleteExtraction = async (id: number) => {
      if (confirm("Are you sure you want to delete this extraction?")) {
        try {
          await api.deleteExtraction(id)
          // Remove from store immediately
          extractionsStore.removeExtraction(id)
          selectedExtractions = selectedExtractions.filter((eid) => eid !== id)
          showToastMessage("Extraction deleted successfully", "success")
        } catch (error) {
          console.error("Failed to delete extraction:", error)
          showToastMessage("Failed to delete extraction", "error")
        }
      }
    }
    ;(window as any).toggleSelection = (id: number) => {
      toggleExtractionSelection(id)
    }
  }
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
        <Button variant="ghost" onclick={refreshData} {loading}>
          <RefreshCw size={16} class="mr-2" />
          Refresh
        </Button>
        <Button
          variant="secondary"
          onclick={() => (showExecuteModal = true)}
          disabled={selectedExtractions.length === 0}
        >
          <Play size={16} class="mr-2" />
          Execute Selected ({selectedExtractions.length})
        </Button>
        <Button
          variant="primary"
          onclick={() => (window.location.href = "/extractions/new")}
        >
          <Plus size={16} class="mr-2" />
          New Extraction
        </Button>
      </div>
    {/snippet}
  </PageHeader>

  <!-- Optimized Filters -->
  <div class="bg-white p-6 rounded-lg shadow">
    <div class="space-y-4">
      <!-- Search Bar -->
      <div class="relative">
        <div
          class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none"
        >
          <Search class="h-5 w-5 text-supabase-gray-400" />
        </div>
        <input
          type="text"
          bind:value={searchTerm}
          placeholder="Search extractions by name, alias, or index..."
          class="block w-full pl-10 pr-10 py-3 border border-supabase-gray-300 rounded-md leading-5 bg-white placeholder-supabase-gray-500 focus:outline-none focus:placeholder-supabase-gray-400 focus:ring-1 focus:ring-supabase-green focus:border-supabase-green"
        />
        {#if searchTerm}
          <button
            onclick={() => {
              searchTerm = ""
            }}
            class="absolute inset-y-0 right-0 pr-3 flex items-center text-supabase-gray-400 hover:text-supabase-gray-600"
          >
            <X class="h-5 w-5" />
          </button>
        {/if}
      </div>

      <!-- Filter Row -->
      <div class="grid grid-cols-1 md:grid-cols-6 gap-4">
        <Input placeholder="Filter by origin..." bind:value={filterOrigin} />
        <Input
          placeholder="Filter by destination..."
          bind:value={filterDestination}
        />
        <Input
          placeholder="Filter by schedule..."
          bind:value={filterSchedule}
        />
        <Select
          placeholder="Filter by type"
          bind:value={filterType}
          options={[
            { value: "", label: "All Types" },
            { value: "db", label: "Database" },
            { value: "http", label: "HTTP API" },
          ]}
        />
        <Select
          placeholder="Filter by incremental"
          bind:value={filterIncremental}
          options={[
            { value: "", label: "All" },
            { value: "true", label: "Incremental" },
            { value: "false", label: "Full" },
          ]}
        />
        <Button variant="ghost" onclick={clearFilters}>Clear Filters</Button>
      </div>

      <!-- Results Summary -->
      <div
        class="flex justify-between items-center text-sm text-supabase-gray-600"
      >
        <div class="space-y-1">
          <span>
            Showing {paginatedExtractions.length} of {totalItems} extractions (filtered
            from {allExtractions.length} total)
          </span>
          <!-- Cache status info -->
          {#if cacheInfo.lastLoaded}
            <div class="text-xs text-supabase-gray-500">
              Last updated: {cacheInfo.lastLoaded.toLocaleTimeString()}
              {#if cacheInfo.isStale}
                <span class="text-yellow-600"
                  >(stale data - consider refreshing)</span
                >
              {/if}
            </div>
          {/if}
          {#if cacheInfo.error}
            <div class="text-xs text-red-600">
              Error: {cacheInfo.error}
            </div>
          {/if}
        </div>
        {#if selectedExtractions.length > 0}
          <div class="flex items-center space-x-4">
            <span class="font-medium">
              {selectedExtractions.length} selected
            </span>
            <div class="flex space-x-2">
              <button
                onclick={selectAllVisible}
                class="text-supabase-green hover:text-supabase-green/80"
              >
                Select visible
              </button>
              <button
                onclick={deselectAllVisible}
                class="text-supabase-green hover:text-supabase-green/80"
              >
                Deselect visible
              </button>
              <button
                onclick={selectAll}
                class="text-supabase-green hover:text-supabase-green/80"
              >
                Select all filtered
              </button>
              <button
                onclick={deselectAll}
                class="text-supabase-green hover:text-supabase-green/80"
              >
                Clear selection
              </button>
            </div>
          </div>
        {/if}
      </div>
    </div>
  </div>

  <!-- Extractions Table -->
  <div class="bg-white shadow rounded-lg">
    <div class="p-6">
      <Table
        {columns}
        data={paginatedExtractions}
        {loading}
        emptyMessage="No extractions found. Try adjusting your filters or search terms."
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

<!-- Execute Modal -->
<Modal bind:open={showExecuteModal} title="Execute Extractions">
  <div class="space-y-4">
    <p class="text-sm text-supabase-gray-600">
      Execute {selectedExtractions.length} selected extraction{selectedExtractions.length !==
      1
        ? "s"
        : ""}.
    </p>

    <div class="bg-supabase-gray-50 p-3 rounded-md max-h-40 overflow-y-auto">
      <h5 class="text-sm font-medium text-supabase-gray-700 mb-2">
        Selected extractions:
      </h5>
      <div class="text-sm text-supabase-gray-600 space-y-1">
        {#each selectedExtractions as id}
          <div>
            • {allExtractions.find((e) => e.id === id)?.extractionName ||
              `ID: ${id}`}
          </div>
        {/each}
      </div>
    </div>

    <Select
      label="Execution Type"
      bind:value={executeType}
      options={[
        { value: "transfer", label: "Transfer (to destination)" },
        { value: "pull", label: "Pull (to CSV)" },
      ]}
    />

    <div class="flex justify-end space-x-3">
      <Button variant="secondary" onclick={() => (showExecuteModal = false)}>
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

<!-- Toast Notifications -->
<Toast bind:show={showToast} type={toastType} message={toastMessage} />
