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
  import ConfirmationModal from "$lib/components/ui/ConfirmationModal.svelte"
  import {
    Plus,
    Play,
    Download,
    Eye,
    Edit,
    Trash2,
    Search,
    X,
    Filter,
    ChevronDown,
    ChevronUp,
  } from "@lucide/svelte"

  // Use server-side pagination instead of client-side
  let extractions = $state<Extraction[]>([])
  let loading = $state(true)
  let totalCount = $state(0)

  // Single filter state object to reduce reactive complexity
  let filters = $state({
    // Text filters
    search: "",
    name: "",
    contains: "",
    origin: "",
    destination: "",
    schedule: "",

    // ID filters
    scheduleId: "",
    originId: "",
    destinationId: "",

    // Select filters
    sourceType: "",
    isIncremental: "",
    isVirtual: "",

    // UI state
    showAdvanced: false,
  })

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

  // Confirmation modal state
  let showConfirmModal = $state(false)
  let confirmAction = $state<() => Promise<void>>(() => Promise.resolve())
  let confirmMessage = $state("")
  let confirmTitle = $state("")
  let confirmLoading = $state(false)

  // Toast notifications
  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  // Available options for filters (loaded from backend)
  let availableOrigins = $state<{ id: number; name: string }[]>([])
  let availableDestinations = $state<{ id: number; name: string }[]>([])
  let availableSchedules = $state<{ id: number; name: string }[]>([])

  // Single derived state for tracking filter changes
  let lastFiltersState = $state("")
  let filterDebounceTimer: NodeJS.Timeout | null = null

  // Mobile-optimized columns
  const mobileColumns = [
    {
      key: "extractionName",
      label: "Extraction",
      render: (value: string, row: Extraction) => {
        const statusColor =
          row.status === "Completed"
            ? "text-green-600 dark:text-green-400"
            : row.status === "Running"
              ? "text-blue-600 dark:text-blue-400"
              : "text-gray-600 dark:text-gray-400"

        const sourceTypeColor =
          row.sourceType === "http"
            ? "bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300"
            : "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300"

        const incrementalBadge = row.isIncremental
          ? "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300"
          : "bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200"

        return `
          <div class="space-y-3">
            <div class="flex items-start justify-between">
              <div class="min-w-0 flex-1 pr-3">
                <h3 class="font-medium text-gray-900 dark:text-white text-base leading-tight break-words">${value}</h3>
                ${row.extractionAlias ? `<p class="text-sm text-gray-600 dark:text-gray-400 mt-1 break-words">${row.extractionAlias}</p>` : ""}
                ${row.indexName ? `<p class="text-xs text-gray-500 dark:text-gray-400 mt-1"><span class="font-medium">Index:</span> ${row.indexName}</p>` : ""}
              </div>
              <div class="flex items-center space-x-1 flex-shrink-0">
                <button onclick="viewExtraction(${row.id})" class="p-2 text-blue-600 hover:text-blue-800 dark:text-blue-400 dark:hover:text-blue-300 hover:bg-blue-50 dark:hover:bg-blue-900/20 rounded-md transition-colors touch-manipulation min-h-[44px] min-w-[44px] flex items-center justify-center" title="View">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"></path><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"></path></svg>
                </button>
                <button onclick="editExtraction(${row.id})" class="p-2 text-green-600 hover:text-green-800 dark:text-green-400 dark:hover:text-green-300 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition-colors touch-manipulation min-h-[44px] min-w-[44px] flex items-center justify-center" title="Edit">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
                </button>
                <button onclick="deleteExtraction(${row.id})" class="p-2 text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition-colors touch-manipulation min-h-[44px] min-w-[44px] flex items-center justify-center" title="Delete">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
                </button>
              </div>
            </div>
            
            <div class="flex items-center">
              <input 
                type="checkbox" 
                ${selectedExtractions.includes(row.id) ? "checked" : ""} 
                onchange="toggleSelection(${row.id})"
                class="h-4 w-4 rounded border-gray-300 dark:border-gray-600 text-supabase-green focus:ring-supabase-green dark:bg-gray-800 cursor-pointer mr-3"
              />
              <div class="flex flex-wrap gap-2 text-xs">
                <span class="inline-flex items-center px-2.5 py-0.5 rounded-full font-medium ${sourceTypeColor}">
                  ${row.sourceType || "db"}
                </span>
                <span class="inline-flex items-center px-2.5 py-0.5 rounded-full font-medium ${incrementalBadge}">
                  ${row.isIncremental ? "Incremental" : "Full Load"}
                </span>
                ${row.isVirtual ? '<span class="inline-flex items-center px-2.5 py-0.5 rounded-full font-medium bg-purple-100 dark:bg-purple-900/30 text-purple-800 dark:text-purple-300">Virtual</span>' : ""}
              </div>
            </div>
            
            <div class="grid grid-cols-1 gap-2 text-xs text-gray-600 dark:text-gray-400">
              <div>
                <span class="font-medium">Origin:</span> ${row.origin?.originName || "Not configured"}
              </div>
              <div>
                <span class="font-medium">Destination:</span> ${row.destination?.destinationName || "Not configured"}
              </div>
              ${row.schedule?.scheduleName ? `<div><span class="font-medium">Schedule:</span> ${row.schedule.scheduleName}</div>` : ""}
              <div>
                <span class="font-medium">ID:</span> ${row.id}
              </div>
            </div>
          </div>
        `
      },
    },
  ]

  // Desktop columns
  const columns = [
    {
      key: "selection",
      label: "",
      width: "40px",
      render: (value: any, row: Extraction) => {
        const isSelected = selectedExtractions.includes(row.id)
        return `
          <div class="flex items-center justify-center">
            <input 
              type="checkbox" 
              ${isSelected ? "checked" : ""} 
              onchange="toggleSelection(${row.id})"
              class="h-4 w-4 rounded border-gray-300 dark:border-gray-600 text-supabase-green focus:ring-supabase-green dark:bg-gray-800 cursor-pointer"
            />
          </div>
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
          http: "bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300",
          db: "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300",
        }
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colors[type as keyof typeof colors] || "bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200"}">${type}</span>`
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
          ? '<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300">Yes</span>'
          : '<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200">No</span>'
      },
    },
    {
      key: "actions",
      label: "Actions",
      render: (value: any, row: Extraction) => {
        return `
          <div class="flex space-x-2">
            <button onclick="viewExtraction(${row.id})" class="text-blue-600 hover:text-blue-800 dark:text-blue-400 dark:hover:text-blue-300 p-1" title="View">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"></path><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"></path></svg>
            </button>
            <button onclick="editExtraction(${row.id})" class="text-green-600 hover:text-green-800 dark:text-green-400 dark:hover:text-green-300 p-1" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteExtraction(${row.id})" class="text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300 p-1" title="Delete">
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
    // Check if message contains a job GUID pattern
    const jobGuidPattern = /Job ID: ([a-f0-9-]{36})/i
    const match = message.match(jobGuidPattern)

    if (match && type === "success") {
      // Format the message with highlighted job GUID
      const jobGuid = match[1]
      const baseMessage = message.replace(jobGuidPattern, "")

      toastMessage = `<div class="toast-content">${baseMessage}<br><span class="job-guid-highlight">Job ID: ${jobGuid}</span><br><small>Click to copy job ID</small></div>`

      // Add click handler to copy job GUID
      setTimeout(() => {
        const toastElement = document.querySelector(".job-guid-highlight")
        if (toastElement) {
          toastElement.onclick = () => {
            navigator.clipboard.writeText(jobGuid).then(() => {
              const originalText = toastElement.textContent
              toastElement.textContent = "Copied!"
              setTimeout(() => {
                toastElement.textContent = originalText
              }, 1000)
            })
          }
        }
      }, 100)

      // Use HTML mode for toast
      showToast = true
      return
    }

    // Regular message without HTML
    toastMessage = message
    toastType = type
    showToast = true
  }

  // Build filters object for API call
  function buildFilters(): Record<string, string> {
    const apiFilters: Record<string, string> = {
      skip: ((currentPage - 1) * pageSize).toString(),
      take: pageSize.toString(),
      sortBy: sortBy,
      sortDirection: sortDirection,
    }

    // Add string filters - safely handle all filter values
    if (filters.search) {
      const searchStr = String(filters.search).trim()
      if (searchStr) apiFilters.search = searchStr
    }
    if (filters.name) {
      const nameStr = String(filters.name).trim()
      if (nameStr) apiFilters.name = nameStr
    }
    if (filters.contains) {
      const containsStr = String(filters.contains).trim()
      if (containsStr) apiFilters.contains = containsStr
    }
    if (filters.origin) {
      const originStr = String(filters.origin).trim()
      if (originStr) apiFilters.origin = originStr
    }
    if (filters.destination) {
      const destinationStr = String(filters.destination).trim()
      if (destinationStr) apiFilters.destination = destinationStr
    }
    if (filters.schedule) {
      const scheduleStr = String(filters.schedule).trim()
      if (scheduleStr) apiFilters.schedule = scheduleStr
    }

    // Add numeric ID filters - safely convert and validate
    if (filters.scheduleId) {
      const scheduleIdStr = String(filters.scheduleId).trim()
      if (
        scheduleIdStr &&
        scheduleIdStr !== "" &&
        !isNaN(Number(scheduleIdStr))
      ) {
        apiFilters.scheduleId = scheduleIdStr
      }
    }
    if (filters.originId) {
      const originIdStr = String(filters.originId).trim()
      if (originIdStr && originIdStr !== "" && !isNaN(Number(originIdStr))) {
        apiFilters.originId = originIdStr
      }
    }
    if (filters.destinationId) {
      const destinationIdStr = String(filters.destinationId).trim()
      if (
        destinationIdStr &&
        destinationIdStr !== "" &&
        !isNaN(Number(destinationIdStr))
      ) {
        apiFilters.destinationId = destinationIdStr
      }
    }

    // Add select filters - ensure they're strings
    if (filters.sourceType && String(filters.sourceType) !== "") {
      apiFilters.sourceType = String(filters.sourceType)
    }
    if (filters.isIncremental && String(filters.isIncremental) !== "") {
      apiFilters.isIncremental = String(filters.isIncremental)
    }
    if (filters.isVirtual && String(filters.isVirtual) !== "") {
      apiFilters.isVirtual = String(filters.isVirtual)
    }

    return apiFilters
  }

  async function loadExtractions() {
    try {
      loading = true
      const apiFilters = buildFilters()
      const response = await api.getExtractions(apiFilters)

      if (response.error) {
        throw new Error(response.information || "Failed to load extractions")
      }

      extractions = response.content || []
      totalCount = response.entityCount || 0
      totalPages = Math.ceil(totalCount / pageSize)
    } catch (error) {
      showToastMessage(`Failed to load extractions: ${error.message}`, "error")
      extractions = []
      totalCount = 0
    } finally {
      loading = false
    }
  }

  async function loadFilterOptions() {
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
  }

  onMount(async () => {
    await Promise.all([loadExtractions(), loadFilterOptions()])
  })

  function clearAllFilters() {
    filters = {
      search: "",
      name: "",
      contains: "",
      origin: "",
      destination: "",
      schedule: "",
      scheduleId: "",
      originId: "",
      destinationId: "",
      sourceType: "",
      isIncremental: "",
      isVirtual: "",
      showAdvanced: filters.showAdvanced,
    }
    currentPage = 1
    selectedExtractions = []
    loadExtractions()
  }

  function hasActiveFilters(): boolean {
    return !!(
      filters.search ||
      filters.name ||
      filters.contains ||
      filters.origin ||
      filters.destination ||
      filters.schedule ||
      filters.scheduleId ||
      filters.originId ||
      filters.destinationId ||
      filters.sourceType ||
      filters.isIncremental ||
      filters.isVirtual
    )
  }

  async function executeExtractions() {
    if (selectedExtractions.length === 0) {
      showToastMessage("Please select at least one extraction", "error")
      return
    }

    executeLoading = true
    try {
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
            `Cannot transfer ${withoutDestination.length} extraction(\s) without destinations: ${withoutDestination.map((e) => e.extractionName).join(", ")}`,
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

      // Use the unified PUT endpoints with IDs parameter
      const apiFilters = {
        ids: selectedExtractions.join(","),
      }

      let response
      if (executeType === "transfer") {
        response = await api.executeTransfer(apiFilters)
      } else {
        response = await api.executePull(apiFilters)
      }

      // Extract job GUID from the response
      let jobGuid = null
      if (response && response.information) {
        // The job GUID should be in the information field
        jobGuid = response.information
      }

      // Show success message with job GUID
      if (jobGuid) {
        showToastMessage(
          `${executeType === "transfer" ? "Transfer" : "Pull"} job started successfully for ${selectedExtractionsData.length} extraction${selectedExtractionsData.length > 1 ? "s" : ""}. Job ID: ${jobGuid}`,
          "success",
        )
      } else {
        showToastMessage(
          `${executeType === "transfer" ? "Transfer" : "Pull"} job started successfully for ${selectedExtractionsData.length} extraction${selectedExtractionsData.length > 1 ? "s" : ""}: ${selectedExtractionsData.map((e) => e.extractionName).join(", ")}`,
          "success",
        )
      }

      showExecuteModal = false
      selectedExtractions = []
    } catch (error) {
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

  // Unified filter change handler with debouncing
  function handleFilterChange() {
    if (filterDebounceTimer) clearTimeout(filterDebounceTimer)

    filterDebounceTimer = setTimeout(() => {
      currentPage = 1
      selectedExtractions = []
      loadExtractions()
    }, 500)
  }

  // Single effect to watch all filter changes
  $effect(() => {
    const currentFiltersState = JSON.stringify(filters)
    if (currentFiltersState !== lastFiltersState && lastFiltersState !== "") {
      handleFilterChange()
    }
    lastFiltersState = currentFiltersState
  })

  // Initialize lastFiltersState on mount
  $effect(() => {
    if (lastFiltersState === "") {
      lastFiltersState = JSON.stringify(filters)
    }
  })

  function showDeleteConfirmation(id: number) {
    const extraction = extractions.find((e) => e.id === id)
    confirmTitle = "Delete Extraction"
    confirmMessage = `Are you sure you want to delete "${extraction?.extractionName || "this extraction"}"? This action cannot be undone and will permanently remove all associated configuration.`
    confirmAction = async () => {
      confirmLoading = true
      try {
        const response = await api.deleteExtraction(id)

        // Handle 204 No Content response (successful deletion)
        if (response?.statusCode === 204 || !response?.error) {
          // Remove from selected extractions if it was selected
          selectedExtractions = selectedExtractions.filter((eid) => eid !== id)

          // Reload the extractions list to reflect the deletion
          await loadExtractions()

          showToastMessage(
            `Extraction "${extraction?.extractionName || `ID: ${id}`}" deleted successfully`,
            "success",
          )
        } else {
          // Handle unexpected response format
          throw new Error(
            response?.information || "Unexpected response from server",
          )
        }
      } catch (error) {
        let errorMessage = "Failed to delete extraction"

        if (error instanceof Error) {
          // Check for specific error types
          if (error.message.includes("404")) {
            errorMessage =
              "Extraction not found - it may have already been deleted"
          } else if (error.message.includes("403")) {
            errorMessage = "You don't have permission to delete this extraction"
          } else if (error.message.includes("409")) {
            errorMessage =
              "Cannot delete extraction - it may be currently in use by running jobs"
          } else if (error.message.includes("500")) {
            errorMessage = "Server error occurred while deleting extraction"
          } else {
            errorMessage = `Failed to delete extraction: ${error.message}`
          }
        }

        showToastMessage(errorMessage, "error")
        throw error
      } finally {
        confirmLoading = false
      }
    }
    showConfirmModal = true
  }

  // Global functions for table actions
  if (typeof window !== "undefined") {
    ;(window as any).viewExtraction = (id: number) => {
      window.location.href = `/extractions/${id}`
    }
    ;(window as any).editExtraction = (id: number) => {
      window.location.href = `/extractions/${id}/edit`
    }
    ;(window as any).deleteExtraction = (id: number) => {
      showDeleteConfirmation(id)
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
      <div class="flex flex-col sm:flex-row gap-2 sm:gap-3">
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
  <div
    class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700"
  >
    <div class="space-y-4">
      <!-- Quick Search -->
      <div class="relative">
        <div
          class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none"
        >
          <Search class="h-5 w-5 text-gray-400 dark:text-gray-500" />
        </div>
        <input
          type="text"
          bind:value={filters.search}
          placeholder="Search across name, alias, and index name..."
          class="block w-full pl-10 pr-10 py-3 border border-gray-300 dark:border-gray-600 rounded-md leading-5 bg-white dark:bg-gray-800 placeholder-gray-500 dark:placeholder-gray-400 text-gray-900 dark:text-white focus:outline-none focus:ring-1 focus:ring-supabase-green focus:border-supabase-green"
        />
        {#if filters.search}
          <button
            onclick={() => {
              filters.search = ""
            }}
            class="absolute inset-y-0 right-0 pr-3 flex items-center text-gray-400 dark:text-gray-500 hover:text-gray-600 dark:hover:text-gray-300"
          >
            <X class="h-5 w-5" />
          </button>
        {/if}
      </div>

      <!-- Quick Filters Row -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Select
          placeholder="Source Type"
          bind:value={filters.sourceType}
          options={[
            { value: "", label: "All Types" },
            { value: "db", label: "Database" },
            { value: "http", label: "HTTP API" },
          ]}
        />
        <Select
          placeholder="Incremental"
          bind:value={filters.isIncremental}
          options={[
            { value: "", label: "All" },
            { value: "true", label: "Incremental" },
            { value: "false", label: "Full Load" },
          ]}
        />
        <Select
          placeholder="Virtual"
          bind:value={filters.isVirtual}
          options={[
            { value: "", label: "All" },
            { value: "true", label: "Virtual" },
            { value: "false", label: "Physical" },
          ]}
        />
      </div>

      <!-- Filter Controls -->
      <div class="flex justify-between items-center">
        <Button
          variant="ghost"
          onclick={() => (filters.showAdvanced = !filters.showAdvanced)}
        >
          <Filter size={16} class="mr-2" />
          Advanced Filters
          {#if filters.showAdvanced}
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
      {#if filters.showAdvanced}
        <div class="border-t border-gray-200 dark:border-gray-700 pt-4 mt-4">
          <h4 class="text-sm font-medium text-gray-900 dark:text-white mb-4">
            Advanced Filters
          </h4>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <!-- Exact Name Filters -->
            <div class="space-y-3">
              <h5
                class="text-xs font-medium text-gray-700 dark:text-gray-300 uppercase tracking-wider"
              >
                Exact Matches
              </h5>
              <Input
                placeholder="Exact extraction name"
                bind:value={filters.name}
                size="sm"
              />
              <Input
                placeholder="Contains names (comma-separated)"
                bind:value={filters.contains}
                size="sm"
              />
            </div>

            <!-- Entity Filters -->
            <div class="space-y-3">
              <h5
                class="text-xs font-medium text-gray-700 dark:text-gray-300 uppercase tracking-wider"
              >
                Related Entities
              </h5>
              <Input
                placeholder="Origin name"
                bind:value={filters.origin}
                size="sm"
              />
              <Input
                placeholder="Destination name"
                bind:value={filters.destination}
                size="sm"
              />
              <Input
                placeholder="Schedule name"
                bind:value={filters.schedule}
                size="sm"
              />
            </div>

            <!-- ID Filters -->
            <div class="space-y-3">
              <h5
                class="text-xs font-medium text-gray-700 dark:text-gray-300 uppercase tracking-wider"
              >
                ID Filters
              </h5>
              <Input
                placeholder="Origin ID"
                type="number"
                bind:value={filters.originId}
                size="sm"
              />
              <Input
                placeholder="Destination ID"
                type="number"
                bind:value={filters.destinationId}
                size="sm"
              />
              <Input
                placeholder="Schedule ID"
                type="number"
                bind:value={filters.scheduleId}
                size="sm"
              />
            </div>
          </div>
        </div>
      {/if}

      <!-- Results Summary -->
      <div
        class="flex justify-between items-center text-sm text-gray-600 dark:text-gray-400 border-t border-gray-200 dark:border-gray-700 pt-4"
      >
        <div class="space-y-1">
          <span>
            Showing {extractions.length} of {totalCount.toLocaleString()} extractions
            {#if hasActiveFilters()}(filtered){/if}
          </span>
          {#if hasActiveFilters()}
            <div class="flex flex-wrap gap-1 mt-1">
              {#if filters.search}
                <Badge variant="info" size="sm">Search: {filters.search}</Badge>
              {/if}
              {#if filters.sourceType}
                <Badge variant="info" size="sm"
                  >Type: {filters.sourceType}</Badge
                >
              {/if}
              {#if filters.isIncremental}
                <Badge variant="info" size="sm"
                  >Incremental: {filters.isIncremental}</Badge
                >
              {/if}
              {#if filters.isVirtual}
                <Badge variant="info" size="sm"
                  >Virtual: {filters.isVirtual}</Badge
                >
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
  <div
    class="bg-white dark:bg-gray-800 shadow-sm rounded-lg border border-gray-200 dark:border-gray-700"
  >
    <div class="p-3 sm:p-6">
      <!-- Mobile view: Card layout -->
      <div class="block sm:hidden space-y-3">
        {#if loading}
          <div class="flex justify-center py-8">
            <svg
              class="animate-spin h-8 w-8 text-gray-500 dark:text-gray-400"
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
        {:else if extractions.length === 0}
          <div class="text-center py-8">
            <svg
              class="mx-auto h-12 w-12 text-gray-400 dark:text-gray-500"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
              />
            </svg>
            <h3 class="mt-2 text-sm font-medium text-gray-900 dark:text-white">
              No extractions found
            </h3>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
              Try adjusting your filters or search terms.
            </p>
          </div>
        {:else}
          {#each extractions as extraction}
            <div
              class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 border border-gray-200 dark:border-gray-600 hover:bg-gray-100 dark:hover:bg-gray-600/50 transition-colors"
            >
              {@html mobileColumns[0].render(
                extraction.extractionName,
                extraction,
              )}
            </div>
          {/each}
        {/if}

        <!-- Mobile Pagination -->
        {#if totalPages > 1}
          <div
            class="flex justify-between items-center pt-4 border-t border-gray-200 dark:border-gray-700 gap-2"
          >
            <Button
              variant="secondary"
              onclick={() => handlePageChange(currentPage - 1)}
              disabled={currentPage <= 1}
              class="min-h-[44px] px-4 py-2 text-sm font-medium"
            >
              Previous
            </Button>
            <span
              class="text-sm text-gray-600 dark:text-gray-400 font-medium px-2"
            >
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
</div>

<!-- Enhanced Execute Modal -->
<Modal bind:open={showExecuteModal} title="Execute Extractions" size="lg">
  <div class="space-y-6">
    <div
      class="bg-blue-50 dark:bg-blue-900/20 border-l-4 border-blue-400 dark:border-blue-600 p-4"
    >
      <div class="flex">
        <div class="flex-shrink-0">
          <svg
            class="h-5 w-5 text-blue-400 dark:text-blue-300"
            fill="currentColor"
            viewBox="0 0 20 20"
          >
            <path
              fill-rule="evenodd"
              d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z"
              clip-rule="evenodd"
            />
          </svg>
        </div>
        <div class="ml-3">
          <h3 class="text-sm font-medium text-blue-800 dark:text-blue-300">
            Execute {selectedExtractions.length} selected extraction{selectedExtractions.length !==
            1
              ? "s"
              : ""}
          </h3>
          <div class="mt-2 text-sm text-blue-700 dark:text-blue-400">
            <p>
              This will start a background job for the selected extractions. You
              can monitor the job progress in the Jobs section.
            </p>
          </div>
        </div>
      </div>
    </div>

    <div
      class="bg-gray-50 dark:bg-gray-800 p-4 rounded-md max-h-60 overflow-y-auto border border-gray-200 dark:border-gray-700"
    >
      <h5 class="text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">
        Selected extractions ({selectedExtractions.length}):
      </h5>
      <div class="text-sm text-gray-600 dark:text-gray-400 space-y-2">
        {#each selectedExtractions as id}
          {@const extraction = extractions.find((e) => e.id === id)}
          <div
            class="flex items-center justify-between p-2 bg-white dark:bg-gray-900 rounded border border-gray-200 dark:border-gray-600"
          >
            <div class="flex-1">
              <span class="font-medium text-gray-900 dark:text-white">
                {extraction?.extractionName || `ID: ${id}`}
              </span>
              <div class="text-xs text-gray-500 dark:text-gray-400 mt-1">
                {extraction?.origin?.originName || "No origin"} →
                {executeType === "transfer"
                  ? extraction?.destination?.destinationName || "No destination"
                  : "CSV File"}
              </div>
            </div>
            <div class="ml-3 flex-shrink-0">
              {#if executeType === "transfer" && !extraction?.destinationId}
                <span
                  class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300"
                >
                  No Destination
                </span>
              {:else if !extraction?.originId}
                <span
                  class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300"
                >
                  No Origin
                </span>
              {:else}
                <span
                  class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300"
                >
                  Ready
                </span>
              {/if}
            </div>
          </div>
        {/each}
      </div>
    </div>

    <div class="space-y-4">
      <Select
        label="Execution Type"
        bind:value={executeType}
        options={[
          { value: "transfer", label: "Transfer (to destination database)" },
          { value: "pull", label: "Pull (to CSV files)" },
        ]}
      />

      <div
        class="bg-yellow-50 dark:bg-yellow-900/20 border-l-4 border-yellow-400 dark:border-yellow-600 p-3"
      >
        <div class="flex">
          <div class="flex-shrink-0">
            <svg
              class="h-5 w-5 text-yellow-400 dark:text-yellow-300"
              fill="currentColor"
              viewBox="0 0 20 20"
            >
              <path
                fill-rule="evenodd"
                d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z"
                clip-rule="evenodd"
              />
            </svg>
          </div>
          <div class="ml-3">
            <p class="text-sm text-yellow-700 dark:text-yellow-400">
              {#if executeType === "transfer"}
                <strong>Transfer mode:</strong> Data will be transferred to the configured
                destination databases.
              {:else}
                <strong>Pull mode:</strong> Data will be extracted and saved as CSV
                files for download.
              {/if}
            </p>
          </div>
        </div>
      </div>
    </div>

    <div
      class="flex flex-col sm:flex-row justify-end space-y-3 sm:space-y-0 sm:space-x-3 pt-4 border-t border-gray-200 dark:border-gray-700"
    >
      <Button
        variant="secondary"
        onclick={() => (showExecuteModal = false)}
        disabled={executeLoading}
      >
        Cancel
      </Button>
      <Button
        variant="primary"
        loading={executeLoading}
        onclick={executeExtractions}
        disabled={executeLoading}
      >
        {#if executeLoading}
          <svg
            class="animate-spin -ml-1 mr-2 h-4 w-4 text-white"
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
          Starting {executeType}...
        {:else}
          <svg
            class="w-4 h-4 mr-2"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M14.828 14.828a4 4 0 01-5.656 0M9 10h1m4 0h1m-6 4h8m-9-4V8a3 3 0 016 0v2M5 12h14l-1 7H6l-1-7z"
            ></path>
          </svg>
          Execute {executeType}
        {/if}
      </Button>
    </div>
  </div>
</Modal>

<!-- Enhanced Confirmation Modal -->
<ConfirmationModal
  bind:open={showConfirmModal}
  title={confirmTitle}
  message={confirmMessage}
  type="danger"
  loading={confirmLoading}
  onConfirm={confirmAction}
  confirmText="Delete Extraction"
  cancelText="Cancel"
/>

<!-- Toast Notifications with HTML support -->
<Toast
  bind:show={showToast}
  type={toastType}
  message={toastMessage}
  allowHtml={true}
/>

<style>
  /* Enhanced toast styling for job notifications */
  :global(.job-guid-toast) {
    font-family: "Monaco", "Menlo", "Ubuntu Mono", monospace;
    background-color: #f8fafc !important;
    border: 1px solid #e2e8f0 !important;
  }

  :global(.job-guid-highlight) {
    background-color: #fef3c7;
    padding: 2px 6px;
    border-radius: 4px;
    font-weight: 600;
    color: #92400e;
    border: 1px solid #fbbf24;
    cursor: pointer;
    transition: all 0.2s ease-in-out;
  }

  :global(.dark .job-guid-highlight) {
    background-color: #451a03;
    color: #fed7aa;
    border-color: #92400e;
  }

  :global(.job-guid-highlight:hover) {
    background-color: #fed7aa;
    border-color: #f97316;
    transform: scale(1.02);
  }

  :global(.dark .job-guid-highlight:hover) {
    background-color: #7c2d12;
    border-color: #ea580c;
  }

  /* Mobile touch targets and accessibility */
  @media (max-width: 640px) {
    /* Ensure all touch targets are at least 44px */
    :global(button) {
      min-height: 44px;
      min-width: 44px;
    }

    /* Improve mobile card hover states */
    :global(.bg-gray-50:hover) {
      background-color: rgb(249 250 251 / 0.8);
    }
    :global(.dark .bg-gray-50:hover) {
      background-color: rgb(55 65 81 / 0.6);
    }

    /* Better text wrapping for mobile */
    :global(.break-words) {
      word-wrap: break-word;
      overflow-wrap: break-word;
      hyphens: auto;
    }

    /* Improve button spacing in mobile cards */
    :global(.mobile-card .flex.space-x-1 > button) {
      margin-left: 0.25rem;
      margin-right: 0.25rem;
    }

    /* Touch-friendly checkbox sizing */
    :global(input[type="checkbox"]) {
      min-height: 20px;
      min-width: 20px;
    }
  }

  /* Enhance mobile typography */
  @media (max-width: 640px) {
    :global(.text-base) {
      line-height: 1.4;
    }
    :global(.text-xs) {
      line-height: 1.3;
    }
    :global(.text-sm) {
      line-height: 1.35;
    }
  }

  /* Better responsive behavior for cards */
  @media (max-width: 640px) {
    :global(.mobile-card) {
      padding: 1rem;
      margin-bottom: 0.75rem;
    }

    /* Ensure action buttons don't get too cramped */
    :global(.mobile-actions) {
      display: flex;
      gap: 0.5rem;
      flex-shrink: 0;
      align-items: flex-start;
    }
  }
</style>
