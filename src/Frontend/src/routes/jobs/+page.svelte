<script lang="ts">
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import { auth } from "$lib/auth.svelte.js"
  import type { JobDto, ExtractionAggregatedDto } from "$lib/types.js"
  import PageHeader from "$lib/components/layout/PageHeader.svelte"
  import Table from "$lib/components/ui/Table.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Input from "$lib/components/ui/Input.svelte"
  import Select from "$lib/components/ui/Select.svelte"
  import Card from "$lib/components/ui/Card.svelte"
  import Toast from "$lib/components/ui/Toast.svelte"
  import ConfirmationModal from "$lib/components/ui/ConfirmationModal.svelte"
  import Badge from "$lib/components/ui/Badge.svelte"
  import {
    Trash2,
    RefreshCw,
    BarChart3,
    Clock,
    CheckCircle,
    XCircle,
    Filter,
    ChevronDown,
    ChevronUp,
    X,
    StopCircle,
    Play,
    Copy,
    Users,
    Database,
  } from "@lucide/svelte"

  // Group jobs by Job GUID interface
  interface JobGroup {
    jobGuid: string
    jobType: string
    status: string
    startTime: string
    endTime?: string
    totalTimeSpentMs: number
    totalMegaBytes: number
    extractions: JobDto[]
    canCancel: boolean
  }

  let healthData = $state<any>(null)
  let metricsData = $state<any>(null)
  let activeJobs = $state<JobDto[]>([])
  let activeJobGroups = $state<JobGroup[]>([])
  let recentJobs = $state<JobDto[]>([])
  let aggregatedJobs = $state<ExtractionAggregatedDto[]>([])
  let loading = $state(true)
  let activeView = $state<"active" | "recent" | "aggregated">("active")

  // Pagination for recent jobs - server-side
  let currentPage = $state(1)
  let totalPages = $state(1)
  let totalItems = $state(0)
  let pageSize = $state(20)

  // Pagination for aggregated jobs - server-side
  let aggregatedCurrentPage = $state(1)
  let aggregatedTotalPages = $state(1)
  let aggregatedTotalItems = $state(0)
  let aggregatedPageSize = $state(20)

  // Mobile filter state
  let showMobileFilters = $state(false)

  // Filters for recent jobs
  let searchTerm = $state("")
  let filterStatus = $state("")
  let filterType = $state("")
  let relativeTime = $state("86400") // Last 24 hours
  let sortKey = $state("")
  let sortDirection = $state<"asc" | "desc">("desc")

  // Confirmation modal state
  let showConfirmModal = $state(false)
  let confirmAction = $state<() => Promise<void>>(() => Promise.resolve())
  let confirmMessage = $state("")
  let confirmTitle = $state("")
  let confirmLoading = $state(false)

  // Job cancellation state
  let cancellingJobs = $state<Set<string>>(new Set())

  // Toast notifications
  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  function showToastMessage(
    message: string,
    type: "success" | "error" | "info" = "info",
  ) {
    toastMessage = message
    toastType = type
    showToast = true
  }

  function formatBytes(bytes: number): string {
    if (bytes === 0) return "0 Bytes"
    const k = 1024
    const sizes = ["Bytes", "KB", "MB", "GB"]
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + " " + sizes[i]
  }

  function formatDuration(ms: number): string {
    const seconds = Math.floor(ms / 1000)
    const minutes = Math.floor(seconds / 60)
    const hours = Math.floor(minutes / 60)

    if (hours > 0) return `${hours}h ${minutes % 60}m`
    if (minutes > 0) return `${minutes}m ${seconds % 60}s`
    return `${seconds}s`
  }

  function copyToClipboard(text: string) {
    navigator.clipboard
      .writeText(text)
      .then(() => {
        showToastMessage("Job GUID copied to clipboard", "success")
      })
      .catch(() => {
        showToastMessage("Failed to copy to clipboard", "error")
      })
  }

  function groupJobsByGuid(jobs: JobDto[]): JobGroup[] {
    const groups = new Map<string, JobGroup>()

    jobs.forEach((job) => {
      if (!groups.has(job.jobGuid)) {
        groups.set(job.jobGuid, {
          jobGuid: job.jobGuid,
          jobType: job.jobType,
          status: job.status,
          startTime: job.startTime,
          endTime: job.endTime,
          totalTimeSpentMs: 0,
          totalMegaBytes: 0,
          extractions: [],
          canCancel: job.status === "Running",
        })
      }

      const group = groups.get(job.jobGuid)!
      group.extractions.push(job)
      group.totalTimeSpentMs += job.timeSpentMs
      group.totalMegaBytes += job.megaBytes

      // Update end time to latest
      if (
        job.endTime &&
        (!group.endTime || new Date(job.endTime) > new Date(group.endTime))
      ) {
        group.endTime = job.endTime
      }
    })

    return Array.from(groups.values()).sort(
      (a, b) =>
        new Date(b.startTime).getTime() - new Date(a.startTime).getTime(),
    )
  }

  // Mobile-optimized Job Group card component
  function renderMobileJobGroup(group: JobGroup): string {
    const statusColor =
      group.status === "Completed"
        ? "text-green-600 dark:text-green-400"
        : group.status === "Running"
          ? "text-blue-600 dark:text-blue-400"
          : "text-red-600 dark:text-red-400"

    const duration = formatDuration(group.totalTimeSpentMs)
    const size = formatBytes(group.totalMegaBytes * 1024 * 1024)
    const startTime = new Date(group.startTime).toLocaleString()

    const extractionsList = group.extractions
      .map(
        (ext) => `
      <div class="flex items-center justify-between text-xs py-1">
        <span class="font-medium text-gray-700 dark:text-gray-300">${ext.name}</span>
        <span class="text-gray-500 dark:text-gray-400">${formatBytes(ext.megaBytes * 1024 * 1024)}</span>
      </div>
    `,
      )
      .join("")

    return `
      <div class="space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-2">
            <span class="text-xs font-mono text-gray-500 dark:text-gray-400">
              ${group.jobGuid.substring(0, 8)}...
            </span>
            <button onclick="copyToClipboard('${group.jobGuid}')" class="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
              <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z"></path>
              </svg>
            </button>
          </div>
          ${
            group.canCancel
              ? `
            <button onclick="showCancelJobConfirmation('${group.jobGuid}')" 
                    class="text-red-500 hover:text-red-700 dark:hover:text-red-400 p-1 rounded-md hover:bg-red-50 dark:hover:bg-red-900/20"
                    title="Cancel job">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6"></path>
              </svg>
            </button>
          `
              : ""
          }
        </div>
        
        <div class="flex flex-wrap gap-2 text-xs">
          <span class="inline-flex items-center px-2 py-1 rounded-full font-medium ${
            group.status === "Completed"
              ? "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300"
              : group.status === "Running"
                ? "bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300"
                : "bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300"
          }">
            ${group.status}
          </span>
          <span class="inline-flex items-center px-2 py-1 rounded-full bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200 font-medium">
            ${group.jobType}
          </span>
          <span class="inline-flex items-center px-2 py-1 rounded-full bg-purple-100 dark:bg-purple-900/30 text-purple-800 dark:text-purple-300 font-medium">
            ${group.extractions.length} extraction${group.extractions.length > 1 ? "s" : ""}
          </span>
        </div>
        
        <div class="grid grid-cols-2 gap-2 text-xs text-gray-600 dark:text-gray-400">
          <div>
            <span class="font-medium">Duration:</span> ${duration}
          </div>
          <div>
            <span class="font-medium">Total Size:</span> ${size}
          </div>
          <div class="col-span-2">
            <span class="font-medium">Started:</span> ${startTime}
          </div>
        </div>
        
        <div class="border-t border-gray-200 dark:border-gray-600 pt-2">
          <div class="text-xs font-medium text-gray-700 dark:text-gray-300 mb-2">Extractions:</div>
          <div class="space-y-1 max-h-32 overflow-y-auto">
            ${extractionsList}
          </div>
        </div>
      </div>
    `
  }

  // Desktop columns for job groups
  const jobGroupColumns = [
    {
      key: "jobGuid",
      label: "Job GUID",
      sortable: true,
      render: (value: string) => `
        <div class="flex items-center space-x-2">
          <span class="font-mono text-sm">${value.substring(0, 8)}...</span>
          <button onclick="copyToClipboard('${value}')" class="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300" title="Copy full GUID">
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z"></path>
            </svg>
          </button>
        </div>
      `,
    },
    { key: "jobType", label: "Type", sortable: true },
    {
      key: "status",
      label: "Status",
      sortable: true,
      render: (value: string) => {
        const variant =
          value === "Completed"
            ? "success"
            : value === "Running"
              ? "info"
              : "error"
        const colors = {
          success:
            "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300",
          info: "bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300",
          error: "bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300",
        }
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colors[variant]}">${value}</span>`
      },
    },
    {
      key: "extractions",
      label: "Extractions",
      sortable: false,
      render: (value: JobDto[], row: JobGroup) => `
        <div class="space-y-1">
          <div class="flex items-center space-x-2">
            <span class="inline-flex items-center px-2 py-1 rounded-full text-xs bg-purple-100 dark:bg-purple-900/30 text-purple-800 dark:text-purple-300 font-medium">
              ${value.length} extraction${value.length > 1 ? "s" : ""}
            </span>
          </div>
          <div class="text-xs space-y-1 max-h-20 overflow-y-auto">
            ${value
              .map(
                (ext) => `
              <div class="flex items-center justify-between">
                <span class="text-gray-700 dark:text-gray-300">${ext.name}</span>
                <span class="text-gray-500 dark:text-gray-400">${formatBytes(ext.megaBytes * 1024 * 1024)}</span>
              </div>
            `,
              )
              .join("")}
          </div>
        </div>
      `,
    },
    {
      key: "totalTimeSpentMs",
      label: "Total Duration",
      sortable: true,
      render: (value: number) => formatDuration(value),
    },
    {
      key: "totalMegaBytes",
      label: "Total Data Size",
      sortable: true,
      render: (value: number) => formatBytes(value * 1024 * 1024),
    },
    {
      key: "startTime",
      label: "Started",
      sortable: true,
      render: (value: string) => new Date(value).toLocaleString(),
    },
    {
      key: "actions",
      label: "Actions",
      sortable: false,
      render: (value: any, row: JobGroup) => {
        if (row.canCancel) {
          return `
            <button onclick="showCancelJobConfirmation('${row.jobGuid}')" 
                    class="inline-flex items-center px-2 py-1 text-xs font-medium text-red-600 dark:text-red-400 hover:text-red-800 dark:hover:text-red-300 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition-colors"
                    title="Cancel job">
              <svg class="w-4 h-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6"></path>
              </svg>
              Cancel
            </button>
          `
        }
        return `<span class="text-gray-400 dark:text-gray-500 text-xs">No actions</span>`
      },
    },
  ]

  // Mobile-optimized columns for aggregated jobs
  const mobileAggregatedColumns = [
    {
      key: "extractionName",
      label: "Extraction",
      sortable: true,
      render: (value: string, row: ExtractionAggregatedDto) => {
        const successRate =
          row.totalJobs > 0
            ? ((row.completedJobs / row.totalJobs) * 100).toFixed(1)
            : "0"
        const totalSize =
          row.totalSizeMB < 1024
            ? `${row.totalSizeMB.toFixed(1)} MB`
            : `${(row.totalSizeMB / 1024).toFixed(1)} GB`
        const lastRun = row.lastEndTime
          ? new Date(row.lastEndTime).toLocaleString()
          : "Never"

        return `
          <div class="space-y-2">
            <div class="font-medium text-gray-900 dark:text-white text-base leading-tight">
              ${value}
            </div>
            <div class="grid grid-cols-2 gap-2 text-xs">
              <div class="col-span-2 flex flex-wrap gap-1">
                <span class="inline-flex items-center px-2 py-1 rounded-full bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300 font-medium">
                  ${row.totalJobs} total
                </span>
                <span class="inline-flex items-center px-2 py-1 rounded-full bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300 font-medium">
                  ${row.completedJobs} completed
                </span>
                ${row.failedJobs > 0 ? `<span class="inline-flex items-center px-2 py-1 rounded-full bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300 font-medium">${row.failedJobs} failed</span>` : ""}
              </div>
            </div>
            <div class="grid grid-cols-2 gap-2 text-xs text-gray-600 dark:text-gray-400">
              <div>
                <span class="font-medium">Data:</span> ${totalSize}
              </div>
              <div>
                <span class="font-medium">Success:</span> ${successRate}%
              </div>
              <div class="col-span-2">
                <span class="font-medium">Last run:</span> ${lastRun}
              </div>
            </div>
          </div>
        `
      },
    },
  ]

  // Desktop columns for aggregated jobs
  const aggregatedColumns = [
    { key: "extractionName", label: "Extraction", sortable: true },
    { key: "totalJobs", label: "Total Jobs", sortable: true },
    {
      key: "totalSizeMB",
      label: "Total Data",
      sortable: true,
      render: (value: number) => {
        if (value < 1024) return `${value.toFixed(1)} MB`
        return `${(value / 1024).toFixed(1)} GB`
      },
    },
    {
      key: "completedJobs",
      label: "Completed",
      sortable: true,
      render: (value: number, row: ExtractionAggregatedDto) => {
        const percentage =
          row.totalJobs > 0 ? ((value / row.totalJobs) * 100).toFixed(1) : "0"
        return `${value} (${percentage}%)`
      },
    },
    {
      key: "failedJobs",
      label: "Failed",
      sortable: true,
      render: (value: number, row: ExtractionAggregatedDto) => {
        const percentage =
          row.totalJobs > 0 ? ((value / row.totalJobs) * 100).toFixed(1) : "0"
        return `${value} (${percentage}%)`
      },
    },
    {
      key: "lastEndTime",
      label: "Last Run",
      sortable: true,
      render: (value: string) =>
        value ? new Date(value).toLocaleString() : "-",
    },
  ]

  // Build filters for recent jobs API call
  function buildRecentJobsFilters(): Record<string, string> {
    const filters: Record<string, string> = {
      take: pageSize.toString(),
      skip: ((currentPage - 1) * pageSize).toString(),
      relativeStart: relativeTime,
    }

    if (searchTerm.trim()) filters.extractionName = searchTerm.trim()
    if (filterStatus) filters.status = filterStatus
    if (filterType) filters.type = filterType
    if (sortKey) {
      filters.sortBy = sortKey
      filters.sortDirection = sortDirection
    }

    return filters
  }

  // Build filters for aggregated jobs API call
  function buildAggregatedJobsFilters(): Record<string, string> {
    const filters: Record<string, string> = {
      take: aggregatedPageSize.toString(),
      skip: ((aggregatedCurrentPage - 1) * aggregatedPageSize).toString(),
      relativeStart: relativeTime,
    }

    if (sortKey) {
      filters.sortBy = sortKey
      filters.sortDirection = sortDirection
    }

    return filters
  }

  onMount(async () => {
    await loadJobs()
    // Set up auto-refresh for active jobs
    const interval = setInterval(async () => {
      if (activeView === "active") {
        await loadActiveJobs()
      }
    }, 5000)

    return () => clearInterval(interval)
  })

  async function loadJobs() {
    await Promise.all([
      loadActiveJobs(),
      loadRecentJobs(),
      loadAggregatedJobs(),
      loadHealthData(),
    ])
    loading = false
  }

  async function loadHealthData() {
    try {
      const [health, metrics] = await Promise.all([
        api.getHealth(),
        api.getMetrics(),
      ])
      healthData = health
      metricsData = metrics
    } catch (error) {
      console.error("Failed to load health data:", error)
    }
  }

  async function loadActiveJobs() {
    try {
      const response = await api.getActiveJobs()
      activeJobs = response.content || []
      activeJobGroups = groupJobsByGuid(activeJobs)
    } catch (error) {
      console.error("Failed to load active jobs:", error)
    }
  }

  async function loadRecentJobs() {
    try {
      const filters = buildRecentJobsFilters()

      const response = await api.searchJobs(filters)

      if (response.error) {
        throw new Error(response.information || "Failed to load recent jobs")
      }

      recentJobs = response.content || []
      totalItems = response.entityCount || 0
      totalPages = Math.ceil(totalItems / pageSize)
    } catch (error) {
      showToastMessage("Failed to load recent jobs", "error")
      recentJobs = []
      totalItems = 0
    }
  }

  async function loadAggregatedJobs() {
    try {
      const filters = buildAggregatedJobsFilters()
      const response = await api.getAggregatedJobs(filters)

      if (response.error) {
        throw new Error(
          response.information || "Failed to load aggregated jobs",
        )
      }

      aggregatedJobs = response.content || []
      aggregatedTotalItems = response.entityCount || 0
      aggregatedTotalPages = Math.ceil(
        aggregatedTotalItems / aggregatedPageSize,
      )
    } catch (error) {
      showToastMessage("Failed to load aggregated jobs", "error")
      aggregatedJobs = []
      aggregatedTotalItems = 0
    }
  }

  async function cancelJob(jobGuid: string) {
    if (cancellingJobs.has(jobGuid)) return

    try {
      cancellingJobs.add(jobGuid)

      // Call the API endpoint to cancel the job
      const response = await api.cancelJob(jobGuid)

      if (!response.error) {
        showToastMessage(
          `Job ${jobGuid.substring(0, 8)}... cancellation requested`,
          "success",
        )
        // Refresh active jobs to reflect the cancellation
        await loadActiveJobs()
      } else {
        throw new Error(response.information || "Failed to cancel job")
      }
    } catch (error) {
      console.error("Cancel job error:", error)
      showToastMessage(`Failed to cancel job: ${error.message}`, "error")
    } finally {
      cancellingJobs.delete(jobGuid)
    }
  }

  function showCancelJobConfirmation(jobGuid: string) {
    const shortGuid = jobGuid.substring(0, 8)
    confirmTitle = "Cancel Job"
    confirmMessage = `Are you sure you want to cancel job ${shortGuid}...? This will stop all running extractions in this job.`
    confirmAction = async () => {
      confirmLoading = true
      try {
        await cancelJob(jobGuid)
      } finally {
        confirmLoading = false
      }
    }
    showConfirmModal = true
  }

  function showClearJobsConfirmation() {
    confirmTitle = "Clear Job History"
    confirmMessage =
      "Are you sure you want to clear all job history? This action cannot be undone and will permanently delete all job records."
    confirmAction = async () => {
      confirmLoading = true
      try {
        await api.clearJobs()
        await loadJobs()
        showToastMessage("Job history cleared successfully", "success")
      } catch (error) {
        showToastMessage("Failed to clear job history", "error")
        throw error
      } finally {
        confirmLoading = false
      }
    }
    showConfirmModal = true
  }

  function handleRecentJobsPageChange(page: number) {
    currentPage = page
    loadRecentJobs()
  }

  function handleRecentJobsPageSizeChange(newPageSize: number) {
    pageSize = newPageSize
    currentPage = 1
    loadRecentJobs()
  }

  function handleAggregatedJobsPageChange(page: number) {
    aggregatedCurrentPage = page
    loadAggregatedJobs()
  }

  function handleAggregatedJobsPageSizeChange(newPageSize: number) {
    aggregatedPageSize = newPageSize
    aggregatedCurrentPage = 1
    loadAggregatedJobs()
  }

  function handleSort(key: string, direction: "asc" | "desc") {
    sortKey = key
    sortDirection = direction

    if (activeView === "recent") {
      currentPage = 1
      loadRecentJobs()
    } else if (activeView === "aggregated") {
      aggregatedCurrentPage = 1
      loadAggregatedJobs()
    }
  }

  async function refreshData() {
    loading = true
    await loadJobs()
    showToastMessage("Data refreshed", "success")
  }

  // Auto-refresh when filters change
  let filterTimeout: NodeJS.Timeout | null = null
  function debounceFilterChange() {
    if (filterTimeout) clearTimeout(filterTimeout)
    filterTimeout = setTimeout(() => {
      currentPage = 1
      loadRecentJobs()
    }, 500)
  }

  function clearFilters() {
    searchTerm = ""
    filterStatus = ""
    filterType = ""
    currentPage = 1
    loadRecentJobs()
    showMobileFilters = false
  }

  function hasActiveFilters(): boolean {
    return !!(searchTerm || filterStatus || filterType)
  }

  // Make functions available globally for onclick handlers
  if (typeof window !== "undefined") {
    window.copyToClipboard = copyToClipboard
    window.showCancelJobConfirmation = showCancelJobConfirmation
  }

  $effect(() => {
    if (searchTerm !== undefined) debounceFilterChange()
  })

  $effect(() => {
    if (filterStatus !== undefined) {
      currentPage = 1
      loadRecentJobs()
    }
  })

  $effect(() => {
    if (filterType !== undefined) {
      currentPage = 1
      loadRecentJobs()
    }
  })

  $effect(() => {
    if (relativeTime !== undefined) {
      currentPage = 1
      aggregatedCurrentPage = 1
      loadRecentJobs()
      loadAggregatedJobs()
    }
  })
</script>

<svelte:head>
  <title>Jobs - Conductor</title>
</svelte:head>

<div class="space-y-4 sm:space-y-6">
  <PageHeader
    title="Jobs"
    description="Monitor extraction job history and performance"
  >
    {#snippet actions()}
      <div class="flex items-center gap-2">
        <Button
          variant="ghost"
          onclick={refreshData}
          disabled={loading}
          class="p-2 h-10 w-10 rounded-md text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 transition-colors disabled:opacity-50"
          title="Refresh data"
        >
          <RefreshCw size={18} class={loading ? "animate-spin" : ""} />
        </Button>
        <Button
          variant="ghost"
          onclick={showClearJobsConfirmation}
          class="p-2 h-10 w-10 rounded-md text-gray-500 dark:text-gray-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 transition-colors"
          title="Clear job history"
        >
          <Trash2 size={18} />
        </Button>
      </div>
    {/snippet}
  </PageHeader>

  <!-- Mobile-optimized Summary Cards -->
  <div class="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-6">
    <Card class="p-3 sm:p-4">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <Clock class="h-6 w-6 sm:h-8 sm:w-8 text-blue-500" />
        </div>
        <div class="ml-2 sm:ml-4 min-w-0">
          <p
            class="text-xs sm:text-sm font-medium text-gray-600 dark:text-gray-400 truncate"
          >
            Active Jobs
          </p>
          <p
            class="text-lg sm:text-2xl font-semibold text-gray-900 dark:text-white"
          >
            {activeJobGroups.length}
          </p>
        </div>
      </div>
    </Card>

    <Card class="p-3 sm:p-4">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <Database class="h-6 w-6 sm:h-8 sm:w-8 text-purple-500" />
        </div>
        <div class="ml-2 sm:ml-4 min-w-0">
          <p
            class="text-xs sm:text-sm font-medium text-gray-600 dark:text-gray-400 truncate"
          >
            Extractions
          </p>
          <p
            class="text-lg sm:text-2xl font-semibold text-gray-900 dark:text-white"
          >
            {activeJobs.length}
          </p>
        </div>
      </div>
    </Card>

    <Card class="p-3 sm:p-4">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <CheckCircle class="h-6 w-6 sm:h-8 sm:w-8 text-green-500" />
        </div>
        <div class="ml-2 sm:ml-4 min-w-0">
          <p
            class="text-xs sm:text-sm font-medium text-gray-600 dark:text-gray-400 truncate"
          >
            Completed
          </p>
          <p
            class="text-lg sm:text-2xl font-semibold text-gray-900 dark:text-white"
          >
            {recentJobs.filter((j) => j.status === "Completed").length}
          </p>
        </div>
      </div>
    </Card>

    <Card class="p-3 sm:p-4">
      <div class="flex items-center">
        <div class="flex-shrink-0">
          <XCircle class="h-6 w-6 sm:h-8 sm:w-8 text-red-500" />
        </div>
        <div class="ml-2 sm:ml-4 min-w-0">
          <p
            class="text-xs sm:text-sm font-medium text-gray-600 dark:text-gray-400 truncate"
          >
            Failed
          </p>
          <p
            class="text-lg sm:text-2xl font-semibold text-gray-900 dark:text-white"
          >
            {recentJobs.filter((j) => j.status === "Failed").length}
          </p>
        </div>
      </div>
    </Card>
  </div>

  <!-- Main Content -->
  <div
    class="bg-white dark:bg-gray-800 shadow-sm sm:shadow rounded-lg border border-gray-200 dark:border-gray-700"
  >
    <!-- Mobile-optimized Tab Navigation -->
    <div class="border-b border-gray-200 dark:border-gray-700">
      <nav
        class="-mb-px flex overflow-x-auto scrollbar-hide"
        style="scrollbar-width: none; -ms-overflow-style: none;"
      >
        <button
          class="flex-shrink-0 py-3 px-3 sm:px-6 border-b-2 font-medium text-sm transition-colors whitespace-nowrap"
          class:border-supabase-green={activeView === "active"}
          class:text-supabase-green={activeView === "active"}
          class:border-transparent={activeView !== "active"}
          class:text-gray-500={activeView !== "active"}
          class:dark:text-gray-400={activeView !== "active"}
          class:hover:text-gray-700={activeView !== "active"}
          class:dark:hover:text-gray-300={activeView !== "active"}
          onclick={() => (activeView = "active")}
        >
          Active Jobs ({activeJobGroups.length})
        </button>
        <button
          class="flex-shrink-0 py-3 px-3 sm:px-6 border-b-2 font-medium text-sm transition-colors whitespace-nowrap"
          class:border-supabase-green={activeView === "recent"}
          class:text-supabase-green={activeView === "recent"}
          class:border-transparent={activeView !== "recent"}
          class:text-gray-500={activeView !== "recent"}
          class:dark:text-gray-400={activeView !== "recent"}
          class:hover:text-gray-700={activeView !== "recent"}
          class:dark:hover:text-gray-300={activeView !== "recent"}
          onclick={() => (activeView = "recent")}
        >
          Recent ({totalItems.toLocaleString()})
        </button>
        <button
          class="flex-shrink-0 py-3 px-3 sm:px-6 border-b-2 font-medium text-sm transition-colors whitespace-nowrap"
          class:border-supabase-green={activeView === "aggregated"}
          class:text-supabase-green={activeView === "aggregated"}
          class:border-transparent={activeView !== "aggregated"}
          class:text-gray-500={activeView !== "aggregated"}
          class:dark:text-gray-400={activeView !== "aggregated"}
          class:hover:text-gray-700={activeView !== "aggregated"}
          class:dark:hover:text-gray-300={activeView !== "aggregated"}
          onclick={() => (activeView = "aggregated")}
        >
          <span class="hidden sm:inline"
            >Aggregated ({aggregatedTotalItems.toLocaleString()})</span
          >
          <span class="sm:hidden"
            >Stats ({aggregatedTotalItems.toLocaleString()})</span
          >
        </button>
      </nav>
    </div>

    <div class="p-3 sm:p-6">
      {#if activeView === "active"}
        <div class="space-y-4">
          {#if activeJobGroups.length === 0}
            <div class="text-center py-8 sm:py-12">
              <Clock
                class="mx-auto h-10 w-10 sm:h-12 sm:w-12 text-gray-400 dark:text-gray-500"
              />
              <h3
                class="mt-2 text-sm font-medium text-gray-900 dark:text-white"
              >
                No active jobs
              </h3>
              <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
                All extractions are currently idle.
              </p>
            </div>
          {:else}
            <!-- Mobile view: Card layout -->
            <div class="block sm:hidden space-y-4">
              {#each activeJobGroups as group}
                <div
                  class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 border border-gray-200 dark:border-gray-600"
                >
                  {@html renderMobileJobGroup(group)}
                </div>
              {/each}
            </div>

            <!-- Desktop view: Table layout -->
            <div class="hidden sm:block">
              <Table
                columns={jobGroupColumns}
                data={activeJobGroups}
                loading={false}
                emptyMessage="No active jobs"
              />
            </div>
          {/if}
        </div>
      {:else if activeView === "recent"}
        <div class="space-y-4">
          <!-- Mobile Filter Toggle -->
          <div class="block sm:hidden">
            <Button
              variant="secondary"
              onclick={() => (showMobileFilters = !showMobileFilters)}
              class="w-full justify-between min-h-[44px] px-4 py-3 text-sm font-medium border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700"
            >
              <div class="flex items-center">
                <Filter size={16} class="mr-2" />
                Filters
                {#if hasActiveFilters()}
                  <span
                    class="ml-2 inline-flex items-center px-2 py-1 rounded-full text-xs bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300 font-medium"
                  >
                    Active
                  </span>
                {/if}
              </div>
              {#if showMobileFilters}
                <ChevronUp size={16} />
              {:else}
                <ChevronDown size={16} />
              {/if}
            </Button>
          </div>

          <!-- Mobile Filters (Collapsible) -->
          {#if showMobileFilters}
            <div
              class="block sm:hidden bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 space-y-3 border border-gray-200 dark:border-gray-600"
            >
              <Input
                placeholder="Search extractions..."
                bind:value={searchTerm}
                size="sm"
              />
              <div class="grid grid-cols-1 gap-3">
                <Select
                  placeholder="Status"
                  bind:value={filterStatus}
                  options={[
                    { value: "", label: "All Statuses" },
                    { value: "Completed", label: "Completed" },
                    { value: "Failed", label: "Failed" },
                    { value: "Running", label: "Running" },
                  ]}
                  size="sm"
                />
                <Select
                  placeholder="Type"
                  bind:value={filterType}
                  options={[
                    { value: "", label: "All Types" },
                    { value: "Transfer", label: "Transfer" },
                    { value: "Fetch", label: "Fetch" },
                  ]}
                  size="sm"
                />
                <Select
                  bind:value={relativeTime}
                  options={[
                    { value: "3600", label: "Last Hour" },
                    { value: "86400", label: "Last 24 Hours" },
                    { value: "604800", label: "Last Week" },
                    { value: "2592000", label: "Last Month" },
                  ]}
                  size="sm"
                />
              </div>
              {#if hasActiveFilters()}
                <Button
                  variant="secondary"
                  onclick={clearFilters}
                  class="w-full min-h-[44px] px-4 py-2 text-sm font-medium border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700"
                >
                  <X size={16} class="mr-2" />
                  Clear Filters
                </Button>
              {/if}
            </div>
          {/if}

          <!-- Desktop Filters -->
          <div class="hidden sm:grid sm:grid-cols-4 gap-4">
            <Input
              placeholder="Search extractions..."
              bind:value={searchTerm}
            />
            <Select
              placeholder="Filter by status"
              bind:value={filterStatus}
              options={[
                { value: "", label: "All Statuses" },
                { value: "Completed", label: "Completed" },
                { value: "Failed", label: "Failed" },
                { value: "Running", label: "Running" },
              ]}
            />
            <Select
              placeholder="Filter by type"
              bind:value={filterType}
              options={[
                { value: "", label: "All Types" },
                { value: "Transfer", label: "Transfer" },
                { value: "Fetch", label: "Fetch" },
              ]}
            />
            <Select
              placeholder="Time range"
              bind:value={relativeTime}
              options={[
                { value: "3600", label: "Last Hour" },
                { value: "86400", label: "Last 24 Hours" },
                { value: "604800", label: "Last Week" },
                { value: "2592000", label: "Last Month" },
              ]}
            />
          </div>

          <!-- Results Summary -->
          <div
            class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-2 text-sm text-gray-600 dark:text-gray-400"
          >
            <span>
              Showing {recentJobs.length} of {totalItems.toLocaleString()} jobs
            </span>
            <span class="hidden sm:inline">
              Page {currentPage} of {totalPages}
            </span>
            <!-- Mobile pagination info -->
            <span class="sm:hidden text-xs">
              Page {currentPage}/{totalPages}
            </span>
          </div>

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
            {:else if recentJobs.length === 0}
              <div class="text-center py-8">
                <div class="text-gray-500 dark:text-gray-400">
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
                  <p class="mt-2 text-sm font-medium">No jobs found</p>
                  <p class="text-xs text-gray-400">
                    Try adjusting your filters
                  </p>
                </div>
              </div>
            {:else}
              {#each groupJobsByGuid(recentJobs) as group}
                <div
                  class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 border border-gray-200 dark:border-gray-600"
                >
                  {@html renderMobileJobGroup(group)}
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
                  onclick={() => handleRecentJobsPageChange(currentPage - 1)}
                  disabled={currentPage <= 1}
                  class="min-h-[44px] px-4 py-2 text-sm font-medium border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
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
                  onclick={() => handleRecentJobsPageChange(currentPage + 1)}
                  disabled={currentPage >= totalPages}
                  class="min-h-[44px] px-4 py-2 text-sm font-medium border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Next
                </Button>
              </div>
            {/if}
          </div>

          <!-- Desktop view: Table layout -->
          <div class="hidden sm:block">
            <Table
              columns={jobGroupColumns}
              data={groupJobsByGuid(recentJobs)}
              {loading}
              emptyMessage="No jobs found for the selected criteria"
              onSort={handleSort}
              {sortKey}
              {sortDirection}
              pagination={{
                currentPage,
                totalPages,
                pageSize,
                totalItems,
                onPageChange: handleRecentJobsPageChange,
                onPageSizeChange: handleRecentJobsPageSizeChange,
              }}
            />
          </div>
        </div>
      {:else if activeView === "aggregated"}
        <div class="space-y-4">
          <div
            class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-2"
          >
            <p class="text-sm text-gray-600 dark:text-gray-400">
              Aggregated statistics for the selected time period
            </p>
            <div class="w-full sm:w-auto">
              <Select
                bind:value={relativeTime}
                options={[
                  { value: "86400", label: "Last 24 Hours" },
                  { value: "604800", label: "Last Week" },
                  { value: "2592000", label: "Last Month" },
                  { value: "7776000", label: "Last 3 Months" },
                ]}
                class="w-full sm:w-auto min-h-[44px]"
              />
            </div>
          </div>

          <!-- Results Summary -->
          <div
            class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-2 text-sm text-gray-600 dark:text-gray-400"
          >
            <span>
              Showing {aggregatedJobs.length} of {aggregatedTotalItems.toLocaleString()}
              extractions
            </span>
            <span class="hidden sm:inline">
              Page {aggregatedCurrentPage} of {aggregatedTotalPages}
            </span>
            <span class="sm:hidden text-xs">
              Page {aggregatedCurrentPage}/{aggregatedTotalPages}
            </span>
          </div>

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
            {:else if aggregatedJobs.length === 0}
              <div class="text-center py-8">
                <div class="text-gray-500 dark:text-gray-400">
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
                      d="M9 19c-5 0-8-3-8-7s3-7 8-7 8 3 8 7-3 7-8 7z"
                    />
                  </svg>
                  <p class="mt-2 text-sm font-medium">No aggregated data</p>
                  <p class="text-xs text-gray-400">
                    No jobs in selected time period
                  </p>
                </div>
              </div>
            {:else}
              {#each aggregatedJobs as job}
                <div
                  class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 border border-gray-200 dark:border-gray-600"
                >
                  {@html mobileAggregatedColumns[0].render(
                    job.extractionName,
                    job,
                  )}
                </div>
              {/each}
            {/if}

            <!-- Mobile Pagination for Aggregated -->
            {#if aggregatedTotalPages > 1}
              <div
                class="flex justify-between items-center pt-4 border-t border-gray-200 dark:border-gray-700 gap-2"
              >
                <Button
                  variant="secondary"
                  onclick={() =>
                    handleAggregatedJobsPageChange(aggregatedCurrentPage - 1)}
                  disabled={aggregatedCurrentPage <= 1}
                  class="min-h-[44px] px-4 py-2 text-sm font-medium border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Previous
                </Button>
                <span
                  class="text-sm text-gray-600 dark:text-gray-400 font-medium px-2"
                >
                  {aggregatedCurrentPage} / {aggregatedTotalPages}
                </span>
                <Button
                  variant="secondary"
                  onclick={() =>
                    handleAggregatedJobsPageChange(aggregatedCurrentPage + 1)}
                  disabled={aggregatedCurrentPage >= aggregatedTotalPages}
                  class="min-h-[44px] px-4 py-2 text-sm font-medium border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Next
                </Button>
              </div>
            {/if}
          </div>

          <!-- Desktop view: Table layout -->
          <div class="hidden sm:block">
            <Table
              columns={aggregatedColumns}
              data={aggregatedJobs}
              {loading}
              emptyMessage="No aggregated data available"
              onSort={handleSort}
              {sortKey}
              {sortDirection}
              pagination={{
                currentPage: aggregatedCurrentPage,
                totalPages: aggregatedTotalPages,
                pageSize: aggregatedPageSize,
                totalItems: aggregatedTotalItems,
                onPageChange: handleAggregatedJobsPageChange,
                onPageSizeChange: handleAggregatedJobsPageSizeChange,
              }}
            />
          </div>
        </div>
      {/if}
    </div>
  </div>
</div>

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

<style>
  /* Hide scrollbar for mobile tab navigation */
  .scrollbar-hide {
    scrollbar-width: none;
    -ms-overflow-style: none;
  }
  .scrollbar-hide::-webkit-scrollbar {
    display: none;
  }

  /* Improve mobile card hover states */
  @media (max-width: 640px) {
    :global(.bg-gray-50:hover) {
      background-color: rgb(249 250 251 / 0.8);
    }
    :global(.dark .bg-gray-50:hover) {
      background-color: rgb(55 65 81 / 0.6);
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
  }

  /* Improve touch targets on mobile */
  @media (max-width: 640px) {
    :global(button) {
      min-height: 44px;
    }
  }

  /* Enhanced job group cards */
  :global(.job-group-card) {
    transition: all 0.2s ease-in-out;
  }

  :global(.job-group-card:hover) {
    transform: translateY(-1px);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
  }

  :global(.dark .job-group-card:hover) {
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  }

  /* Copy button animations */
  :global(.copy-button) {
    transition: all 0.15s ease-in-out;
  }

  :global(.copy-button:hover) {
    transform: scale(1.1);
  }

  /* Cancel button styling */
  :global(.cancel-job-button) {
    transition: all 0.15s ease-in-out;
  }

  :global(.cancel-job-button:hover) {
    transform: scale(1.05);
  }

  :global(.cancel-job-button:active) {
    transform: scale(0.95);
  }
</style>
