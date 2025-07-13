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
    RefreshCw,
    Filter,
    ChevronDown,
    ChevronUp,
  } from "@lucide/svelte"

  // Use server-side pagination instead of client-side
  let extractions = $state<Extraction[]>([])
  let loading = $state(true)
  let totalCount = $state(0)

  // Enhanced filters matching backend capabilities - with proper initialization
  let searchTerm = $state("")
  let filterName = $state("")
  let filterContains = $state("")
  let filterOrigin = $state("")
  let filterDestination = $state("")
  let filterSchedule = $state("")
  let filterScheduleId = $state("")
  let filterOriginId = $state("")
  let filterDestinationId = $state("")
  let filterSourceType = $state("")
  let filterIncremental = $state("")
  let filterVirtual = $state("")
  let showAdvancedFilters = $state(false)

  // Pagination - use server-side
  let currentPage = $state(1)
  let pageSize = $state(20)
  let totalPages = $state(1)

  // Sorting - use server-side
  let sortBy = $state("id")
  let sortDirection = $state<"asc" | "desc">("desc")

  // Modal states
  let showExecuteModal = $state(false)
  let executeType = $state<"transfer" | "pull">("transfer")
  let executeLoading = $state(false)
  let selectedExtractions = $state<number[]>([])

  // Toast notifications
  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  // Available options for filters (loaded from backend)
  let availableOrigins = $state<{ id: number; name: string }[]>([])
  let availableDestinations = $state<{ id: number; name: string }[]>([])
  let availableSchedules = $state<{ id: number; name: string }[]>([])

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
        const colors = {
          http: "bg-blue-100 text-blue-800",
          db: "bg-green-100 text-green-800",
        }
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colors[type as keyof typeof colors] || "bg-gray-100 text-gray-800"}">${type}</span>`
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

  // Build filters object for API call - with better numeric handling
  function buildFilters(): Record<string, string> {
    const filters: Record<string, string> = {
      skip: ((currentPage - 1) * pageSize).toString(),
      take: pageSize.toString(),
      sortBy: sortBy,
      sortDirection: sortDirection,
    }

    // Helper function to safely add string filters
    function addStringFilter(key: string, value: string) {
      if (value && value.trim && value.trim() !== "") {
        filters[key] = value.trim()
        console.log(`Added string filter: ${key} = ${value.trim()}`)
      }
    }

    // Helper function to safely add numeric filters - FIXED
    function addNumericFilter(key: string, value: string) {
      console.log(
        `Checking numeric filter: ${key} = "${value}" (type: ${typeof value})`,
      )

      if (value !== undefined && value !== null && value !== "") {
        const stringValue = String(value).trim()
        console.log(`After string conversion and trim: "${stringValue}"`)

        // Validate that it's actually a number
        if (!isNaN(Number(stringValue)) && stringValue !== "") {
          filters[key] = stringValue
          console.log(`✅ Added numeric filter: ${key} = ${stringValue}`)
        } else {
          console.log(
            `❌ Skipped invalid numeric filter: ${key} = ${stringValue} (not a number)`,
          )
        }
      } else {
        console.log(`❌ Skipped empty numeric filter: ${key} = ${value}`)
      }
    }

    // Add active filters with proper validation
    addStringFilter("search", searchTerm)
    addStringFilter("name", filterName)
    addStringFilter("contains", filterContains)
    addStringFilter("origin", filterOrigin)
    addStringFilter("destination", filterDestination)
    addStringFilter("schedule", filterSchedule)

    // Add numeric ID filters with validation
    addNumericFilter("scheduleId", filterScheduleId)
    addNumericFilter("originId", filterOriginId)
    addNumericFilter("destinationId", filterDestinationId)

    // Add select filters (these should never be null/undefined from selects)
    if (filterSourceType && filterSourceType !== "") {
      filters.sourceType = filterSourceType
      console.log(`Added select filter: sourceType = ${filterSourceType}`)
    }
    if (filterIncremental && filterIncremental !== "") {
      filters.isIncremental = filterIncremental
      console.log(`Added select filter: isIncremental = ${filterIncremental}`)
    }
    if (filterVirtual && filterVirtual !== "") {
      filters.isVirtual = filterVirtual
      console.log(`Added select filter: isVirtual = ${filterVirtual}`)
    }

    console.log("=== FINAL FILTERS ===")
    console.log("Built filters:", filters)
    console.log("Filter values check:", {
      filterScheduleId: `"${filterScheduleId}"`,
      filterOriginId: `"${filterOriginId}"`,
      filterDestinationId: `"${filterDestinationId}"`,
      types: {
        filterScheduleId: typeof filterScheduleId,
        filterOriginId: typeof filterOriginId,
        filterDestinationId: typeof filterDestinationId,
      },
    })

    return filters
  }

  async function loadExtractions() {
    try {
      loading = true
      const filters = buildFilters()

      console.log("Loading extractions with filters:", filters)

      const response = await api.getExtractions(filters)

      if (response.error) {
        throw new Error(response.information || "Failed to load extractions")
      }

      extractions = response.content || []
      totalCount = response.entityCount || 0
      totalPages = Math.ceil(totalCount / pageSize)

      console.log("Loaded extractions:", {
        count: extractions.length,
        totalCount,
        totalPages,
      })
    } catch (error) {
      console.error("Failed to load extractions:", error)
      showToastMessage(`Failed to load extractions: ${error.message}`, "error")
      extractions = []
      totalCount = 0
    } finally {
      loading = false
    }
  }

  // Load filter options
  async function loadFilterOptions() {
    try {
      const [originsRes, destinationsRes, schedulesRes] = await Promise.all([
        api.getOrigins({ take: "1000" }),
        api.getDestinations({ take: "1000" }),
        api.getSchedules({ take: "1000" }),
      ])

      availableOrigins = (originsRes.content || []).map((o) => ({
        id: o.id,
        name: o.originName,
      }))

      availableDestinations = (destinationsRes.content || []).map((d) => ({
        id: d.id,
        name: d.destinationName,
      }))

      availableSchedules = (schedulesRes.content || []).map((s) => ({
        id: s.id,
        name: s.scheduleName,
      }))
    } catch (error) {
      console.error("Failed to load filter options:", error)
    }
  }

  onMount(async () => {
    await Promise.all([loadExtractions(), loadFilterOptions()])
  })

  async function refreshData() {
    await loadExtractions()
    showToastMessage("Data refreshed successfully", "success")
  }

  function clearAllFilters() {
    // Reset all filters to empty strings
    searchTerm = ""
    filterName = ""
    filterContains = ""
    filterOrigin = ""
    filterDestination = ""
    filterSchedule = ""
    filterScheduleId = ""
    filterOriginId = ""
    filterDestinationId = ""
    filterSourceType = ""
    filterIncremental = ""
    filterVirtual = ""
    currentPage = 1

    // Reload with cleared filters
    loadExtractions()
  }

  function hasActiveFilters(): boolean {
    return !!(
      searchTerm ||
      filterName ||
      filterContains ||
      filterOrigin ||
      filterDestination ||
      filterSchedule ||
      filterScheduleId ||
      filterOriginId ||
      filterDestinationId ||
      filterSourceType ||
      filterIncremental ||
      filterVirtual
    )
  }

  // Simplified validation - just allow execution
  function validateExecutionSelection(): { valid: boolean; issues: string[] } {
    if (selectedExtractions.length === 0) {
      return { valid: false, issues: ["No extractions selected"] }
    }

    // Always return valid for now since ID-based execution is working
    return { valid: true, issues: [] }
  }

  // Reactive validation for the modal - ensure it updates when execute type changes
  const executionValidation = $derived(() => {
    try {
      const result = validateExecutionSelection()
      console.log("Reactive validation triggered:", result)
      return result
    } catch (error) {
      console.error("Validation error:", error)
      return { valid: false, issues: ["Validation error occurred"] }
    }
  })

  async function executeExtractions() {
    if (selectedExtractions.length === 0) {
      showToastMessage("Please select at least one extraction", "error")
      return
    }

    executeLoading = true
    try {
      // Get the actual extraction objects for validation and display
      const selectedExtractionsData = selectedExtractions
        .map((id) => extractions.find((e) => e.id === id))
        .filter(Boolean) as Extraction[]

      if (selectedExtractionsData.length === 0) {
        showToastMessage("No valid extractions selected", "error")
        return
      }

      // Validate selections before execution
      if (executeType === "transfer") {
        const withoutDestination = selectedExtractionsData.filter(
          (e) => !e.destinationId,
        )
        if (withoutDestination.length > 0) {
          showToastMessage(
            `Cannot transfer ${withoutDestination.length} extraction(s) without destinations: ${withoutDestination.map((e) => e.extractionName).join(", ")}`,
            "error",
          )
          return
        }
      }

      const withoutOrigin = selectedExtractionsData.filter((e) => !e.originId)
      if (withoutOrigin.length > 0) {
        showToastMessage(
          `Cannot execute ${withoutOrigin.length} extraction(s) without origins: ${withoutOrigin.map((e) => e.extractionName).join(", ")}`,
          "error",
        )
        return
      }

      // Use the new ID-based filtering approach
      const filters = {
        ids: selectedExtractions.join(","),
      }

      console.log("Executing with ID filters:", filters)

      if (executeType === "transfer") {
        await api.executeTransfer(filters)
        showToastMessage(
          `Transfer job started successfully for ${selectedExtractionsData.length} extraction${selectedExtractionsData.length > 1 ? "s" : ""}: ${selectedExtractionsData.map((e) => e.extractionName).join(", ")}`,
          "success",
        )
      } else {
        await api.executePull(filters)
        showToastMessage(
          `Pull job started successfully for ${selectedExtractionsData.length} extraction${selectedExtractionsData.length > 1 ? "s" : ""}: ${selectedExtractionsData.map((e) => e.extractionName).join(", ")}`,
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
    const visibleIds = extractions.map((e) => e.id)
    selectedExtractions = [...new Set([...selectedExtractions, ...visibleIds])]
  }

  function deselectAllVisible() {
    const visibleIds = extractions.map((e) => e.id)
    selectedExtractions = selectedExtractions.filter(
      (id) => !visibleIds.includes(id),
    )
  }

  function deselectAll() {
    selectedExtractions = []
  }

  function handlePageChange(page: number) {
    currentPage = page
    selectedExtractions = [] // Clear selections when changing pages
    loadExtractions()
  }

  function handlePageSizeChange(newPageSize: number) {
    pageSize = newPageSize
    currentPage = 1
    selectedExtractions = []
    loadExtractions()
  }

  function handleSort(key: string, direction: "asc" | "desc") {
    sortBy = key
    sortDirection = direction
    currentPage = 1
    loadExtractions()
  }

  // Debounce filter changes with null safety
  let filterTimeout: NodeJS.Timeout | null = null
  function debounceFilterChange() {
    if (filterTimeout) clearTimeout(filterTimeout)
    filterTimeout = setTimeout(() => {
      currentPage = 1
      loadExtractions()
    }, 500)
  }

  // Watch filter changes with proper null checking and immediate filtering for IDs
  $effect(() => {
    if (searchTerm !== undefined && searchTerm !== null) debounceFilterChange()
  })

  $effect(() => {
    if (filterName !== undefined && filterName !== null) debounceFilterChange()
  })

  $effect(() => {
    if (filterContains !== undefined && filterContains !== null)
      debounceFilterChange()
  })

  $effect(() => {
    if (filterOrigin !== undefined && filterOrigin !== null)
      debounceFilterChange()
  })

  $effect(() => {
    if (filterDestination !== undefined && filterDestination !== null)
      debounceFilterChange()
  })

  $effect(() => {
    if (filterSchedule !== undefined && filterSchedule !== null)
      debounceFilterChange()
  })

  // ID filters should trigger immediate filtering (not debounced) for better UX
  $effect(() => {
    if (filterScheduleId !== undefined && filterScheduleId !== null) {
      currentPage = 1
      loadExtractions()
    }
  })

  $effect(() => {
    if (filterOriginId !== undefined && filterOriginId !== null) {
      currentPage = 1
      loadExtractions()
    }
  })

  $effect(() => {
    if (filterDestinationId !== undefined && filterDestinationId !== null) {
      currentPage = 1
      loadExtractions()
    }
  })

  $effect(() => {
    if (filterSourceType !== undefined && filterSourceType !== null) {
      currentPage = 1
      loadExtractions()
    }
  })

  $effect(() => {
    if (filterIncremental !== undefined && filterIncremental !== null) {
      currentPage = 1
      loadExtractions()
    }
  })

  $effect(() => {
    if (filterVirtual !== undefined && filterVirtual !== null) {
      currentPage = 1
      loadExtractions()
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
          selectedExtractions = selectedExtractions.filter((eid) => eid !== id)
          await loadExtractions() // Reload to get updated count
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

  <!-- Enhanced Filters -->
  <div class="bg-white p-6 rounded-lg shadow">
    <div class="space-y-4">
      <!-- Quick Search -->
      <div class="relative">
        <div
          class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none"
        >
          <Search class="h-5 w-5 text-supabase-gray-400" />
        </div>
        <input
          type="text"
          bind:value={searchTerm}
          placeholder="Search across name, alias, and index name..."
          class="block w-full pl-10 pr-10 py-3 border border-supabase-gray-300 rounded-md leading-5 bg-white placeholder-supabase-gray-500 focus:outline-none focus:ring-1 focus:ring-supabase-green focus:border-supabase-green"
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

      <!-- Quick Filters Row -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Select
          placeholder="Source Type"
          bind:value={filterSourceType}
          options={[
            { value: "", label: "All Types" },
            { value: "db", label: "Database" },
            { value: "http", label: "HTTP API" },
          ]}
        />
        <Select
          placeholder="Incremental"
          bind:value={filterIncremental}
          options={[
            { value: "", label: "All" },
            { value: "true", label: "Incremental" },
            { value: "false", label: "Full Load" },
          ]}
        />
        <Select
          placeholder="Virtual"
          bind:value={filterVirtual}
          options={[
            { value: "", label: "All" },
            { value: "true", label: "Virtual" },
            { value: "false", label: "Physical" },
          ]}
        />
      </div>

      <!-- Simplified Filter Controls -->
      <div class="flex justify-between items-center">
        <Button
          variant="ghost"
          onclick={() => (showAdvancedFilters = !showAdvancedFilters)}
        >
          <Filter size={16} class="mr-2" />
          Advanced Filters
          {#if showAdvancedFilters}
            <ChevronUp size={16} class="ml-1" />
          {:else}
            <ChevronDown size={16} class="ml-1" />
          {/if}
        </Button>

        {#if hasActiveFilters()}
          <Button variant="ghost" onclick={clearAllFilters}>
            <X size={16} class="mr-2" />
            Clear All Filters
          </Button>
        {/if}
      </div>

      <!-- Advanced Filters -->
      {#if showAdvancedFilters}
        <div class="border-t border-supabase-gray-200 pt-4 mt-4">
          <h4 class="text-sm font-medium text-supabase-gray-900 mb-4">
            Advanced Filters
          </h4>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <!-- Exact Name Filters -->
            <div class="space-y-3">
              <h5
                class="text-xs font-medium text-supabase-gray-700 uppercase tracking-wider"
              >
                Exact Matches
              </h5>
              <Input
                placeholder="Exact extraction name"
                bind:value={filterName}
                size="sm"
              />
              <Input
                placeholder="Contains names (comma-separated)"
                bind:value={filterContains}
                size="sm"
              />
            </div>

            <!-- Entity Filters -->
            <div class="space-y-3">
              <h5
                class="text-xs font-medium text-supabase-gray-700 uppercase tracking-wider"
              >
                Related Entities
              </h5>
              <Input
                placeholder="Origin name"
                bind:value={filterOrigin}
                size="sm"
              />
              <Input
                placeholder="Destination name"
                bind:value={filterDestination}
                size="sm"
              />
              <Input
                placeholder="Schedule name"
                bind:value={filterSchedule}
                size="sm"
              />
            </div>

            <!-- ID Filters -->
            <div class="space-y-3">
              <h5
                class="text-xs font-medium text-supabase-gray-700 uppercase tracking-wider"
              >
                ID Filters
              </h5>
              <Input
                placeholder="Origin ID"
                type="number"
                bind:value={filterOriginId}
                size="sm"
              />
              <Input
                placeholder="Destination ID"
                type="number"
                bind:value={filterDestinationId}
                size="sm"
              />
              <Input
                placeholder="Schedule ID"
                type="number"
                bind:value={filterScheduleId}
                size="sm"
              />
            </div>
          </div>
        </div>
      {/if}

      <!-- Results Summary -->
      <div
        class="flex justify-between items-center text-sm text-supabase-gray-600 border-t border-supabase-gray-200 pt-4"
      >
        <div class="space-y-1">
          <span>
            Showing {extractions.length} of {totalCount.toLocaleString()} extractions
            {#if hasActiveFilters()}(filtered){/if}
          </span>
          {#if hasActiveFilters()}
            <div class="flex flex-wrap gap-1 mt-1">
              {#if searchTerm}
                <Badge variant="info" size="sm">Search: {searchTerm}</Badge>
              {/if}
              {#if filterSourceType}
                <Badge variant="info" size="sm">Type: {filterSourceType}</Badge>
              {/if}
              {#if filterIncremental}
                <Badge variant="info" size="sm"
                  >Incremental: {filterIncremental}</Badge
                >
              {/if}
              {#if filterVirtual}
                <Badge variant="info" size="sm">Virtual: {filterVirtual}</Badge>
              {/if}
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
                Select page
              </button>
              <button
                onclick={deselectAllVisible}
                class="text-supabase-green hover:text-supabase-green/80"
              >
                Deselect page
              </button>
              <button
                onclick={deselectAll}
                class="text-supabase-green hover:text-supabase-green/80"
              >
                Clear all
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
        data={extractions}
        {loading}
        emptyMessage="No extractions found. Try adjusting your filters or search terms."
        onSort={handleSort}
        sortKey={sortBy}
        {sortDirection}
        pagination={{
          currentPage,
          totalPages,
          pageSize,
          totalItems: totalCount,
          onPageChange: handlePageChange,
          onPageSizeChange: handlePageSizeChange,
        }}
      />
    </div>
  </div>
</div>

<!-- Simplified Execute Modal -->
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
          {@const extraction = extractions.find((e) => e.id === id)}
          <div class="flex items-center justify-between">
            <span>• {extraction?.extractionName || `ID: ${id}`}</span>
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
