<script lang="ts">
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
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
  } from "@lucide/svelte"

  let allExtractions = $state<Extraction[]>([])
  let displayedExtractions = $state<Extraction[]>([])
  let loading = $state(true)
  let searchLoading = $state(false)

  // Search and filter states
  let searchTerm = $state("")
  let filterOrigin = $state("")
  let filterDestination = $state("")
  let filterSchedule = $state("")
  let filterType = $state("")
  let filterIncremental = $state("")
  let debouncedSearchTerm = $state("")

  // Modal and execution states
  let showExecuteModal = $state(false)
  let executeType = $state<"transfer" | "pull">("transfer")
  let executeLoading = $state(false)
  let selectedExtractions = $state<number[]>([])

  // Pagination
  let currentPage = $state(1)
  let totalPages = $state(1)
  let totalItems = $state(0)
  let pageSize = $state(50) // Increased for better performance
  let sortKey = $state("")
  let sortDirection = $state<"asc" | "desc">("asc")

  // Toast notifications
  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  // Debounce timer
  let searchDebounceTimer: NodeJS.Timeout | null = null

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
    await loadInitialExtractions()
  })

  async function loadInitialExtractions() {
    try {
      loading = true
      const filters: Record<string, string> = {
        take: "1000", // Load more initially for better client-side filtering
        skip: "0",
      }

      const response = await api.getExtractions(filters)
      allExtractions = response.content || []
      applyClientSideFilters()

      totalItems = response.entityCount || allExtractions.length
      totalPages = Math.ceil(totalItems / pageSize)
    } catch (error) {
      console.error("Failed to load extractions:", error)
      showToastMessage(
        "Failed to load extractions. Please check your connection and try again.",
        "error",
      )
    } finally {
      loading = false
    }
  }

  // Debounced search function
  function debounceSearch(term: string) {
    if (searchDebounceTimer) {
      clearTimeout(searchDebounceTimer)
    }

    searchDebounceTimer = setTimeout(() => {
      debouncedSearchTerm = term
      handleSearch()
    }, 300)
  }

  async function handleSearch() {
    // First apply client-side filters
    applyClientSideFilters()

    // If no results found in current data and we have a search term, try server search
    if (displayedExtractions.length === 0 && debouncedSearchTerm.trim()) {
      await performServerSearch()
    }
  }

  async function performServerSearch() {
    try {
      searchLoading = true
      const filters: Record<string, string> = {
        take: "1000",
      }

      if (debouncedSearchTerm.trim()) {
        // Use 'contains' for partial matching
        filters.contains = debouncedSearchTerm
      }
      if (filterOrigin) filters.origin = filterOrigin
      if (filterDestination) filters.destination = filterDestination
      if (filterSchedule) filters.schedule = filterSchedule

      const response = await api.getExtractions(filters)
      const serverResults = response.content || []

      // Merge server results with existing data, avoiding duplicates
      const existingIds = new Set(allExtractions.map((e) => e.id))
      const newExtractions = serverResults.filter((e) => !existingIds.has(e.id))

      if (newExtractions.length > 0) {
        allExtractions = [...allExtractions, ...newExtractions]
        applyClientSideFilters()
        showToastMessage(
          `Found ${newExtractions.length} additional extractions from server`,
          "info",
        )
      } else if (serverResults.length === 0) {
        showToastMessage(
          "No extractions found matching your search criteria",
          "info",
        )
      }
    } catch (error) {
      console.error("Failed to search extractions:", error)
      showToastMessage("Search failed. Please try again.", "error")
    } finally {
      searchLoading = false
    }
  }

  function applyClientSideFilters() {
    let filtered = [...allExtractions]

    // Apply text search filter
    if (debouncedSearchTerm.trim()) {
      const searchLower = debouncedSearchTerm.toLowerCase()
      filtered = filtered.filter(
        (e) =>
          e.extractionName.toLowerCase().includes(searchLower) ||
          e.extractionAlias?.toLowerCase().includes(searchLower) ||
          e.indexName?.toLowerCase().includes(searchLower),
      )
    }

    // Apply origin filter
    if (filterOrigin.trim()) {
      const originLower = filterOrigin.toLowerCase()
      filtered = filtered.filter((e) =>
        e.origin?.originName.toLowerCase().includes(originLower),
      )
    }

    // Apply destination filter
    if (filterDestination.trim()) {
      const destLower = filterDestination.toLowerCase()
      filtered = filtered.filter((e) =>
        e.destination?.destinationName.toLowerCase().includes(destLower),
      )
    }

    // Apply schedule filter
    if (filterSchedule.trim()) {
      const scheduleLower = filterSchedule.toLowerCase()
      filtered = filtered.filter((e) =>
        e.schedule?.scheduleName.toLowerCase().includes(scheduleLower),
      )
    }

    // Apply type filter
    if (filterType) {
      filtered = filtered.filter((e) => (e.sourceType || "db") === filterType)
    }

    // Apply incremental filter
    if (filterIncremental) {
      const isIncremental = filterIncremental === "true"
      filtered = filtered.filter((e) => e.isIncremental === isIncremental)
    }

    // Apply sorting
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

    // Apply pagination
    const startIndex = (currentPage - 1) * pageSize
    const endIndex = startIndex + pageSize
    displayedExtractions = filtered.slice(startIndex, endIndex)

    // Update pagination info
    totalItems = filtered.length
    totalPages = Math.ceil(totalItems / pageSize)

    // Clear selections that are no longer visible
    const visibleIds = displayedExtractions.map((e) => e.id)
    selectedExtractions = selectedExtractions.filter((id) =>
      visibleIds.includes(id),
    )
  }

  function clearFilters() {
    searchTerm = ""
    debouncedSearchTerm = ""
    filterOrigin = ""
    filterDestination = ""
    filterSchedule = ""
    filterType = ""
    filterIncremental = ""
    currentPage = 1

    if (searchDebounceTimer) {
      clearTimeout(searchDebounceTimer)
    }

    applyClientSideFilters()
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
    const visibleIds = displayedExtractions.map((e) => e.id)
    selectedExtractions = [...new Set([...selectedExtractions, ...visibleIds])]
  }

  function deselectAllVisible() {
    const visibleIds = displayedExtractions.map((e) => e.id)
    selectedExtractions = selectedExtractions.filter(
      (id) => !visibleIds.includes(id),
    )
  }

  function selectAll() {
    selectedExtractions = allExtractions.map((e) => e.id)
  }

  function deselectAll() {
    selectedExtractions = []
  }

  function handlePageChange(page: number) {
    currentPage = page
    applyClientSideFilters()
  }

  function handlePageSizeChange(newPageSize: number) {
    pageSize = newPageSize
    currentPage = 1
    applyClientSideFilters()
  }

  function handleSort(key: string, direction: "asc" | "desc") {
    sortKey = key
    sortDirection = direction
    currentPage = 1
    applyClientSideFilters()
  }

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
          // Remove from local data
          allExtractions = allExtractions.filter((e) => e.id !== id)
          applyClientSideFilters()
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

  // Watch for search term changes
  $effect(() => {
    if (searchTerm !== debouncedSearchTerm) {
      debounceSearch(searchTerm)
    }
  })

  // Watch for filter changes
  $effect(() => {
    currentPage = 1
    applyClientSideFilters()
  })
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

  <!-- Advanced Filters -->
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
        {#if searchLoading}
          <div class="absolute inset-y-0 right-0 pr-3 flex items-center">
            <svg
              class="animate-spin h-5 w-5 text-supabase-gray-400"
              fill="none"
              viewBox="0 0 24 24"
            >
              <circle
                class="opacity-25"
                cx="12"
                cy="12"
                r="10"
                stroke="currentColor"
                stroke-width="4"
              ></circle>
              <path
                class="opacity-75"
                fill="currentColor"
                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
              ></path>
            </svg>
          </div>
        {:else if searchTerm}
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
        <Button variant="ghost" onclick={clearFilters} class="self-end">
          Clear Filters
        </Button>
      </div>

      <!-- Results Summary -->
      <div
        class="flex justify-between items-center text-sm text-supabase-gray-600"
      >
        <span>
          Showing {displayedExtractions.length} of {totalItems} extractions
          {totalItems !== allExtractions.length
            ? `(${allExtractions.length} loaded)`
            : ""}
        </span>
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
                Select all
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
        data={displayedExtractions}
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
