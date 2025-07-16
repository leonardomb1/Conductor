<script lang="ts">
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import type { Schedule } from "$lib/types.js"
  import PageHeader from "$lib/components/layout/PageHeader.svelte"
  import Table from "$lib/components/ui/Table.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Input from "$lib/components/ui/Input.svelte"
  import Modal from "$lib/components/ui/Modal.svelte"
  import Select from "$lib/components/ui/Select.svelte"
  import Toast from "$lib/components/ui/Toast.svelte"
  import ConfirmationModal from "$lib/components/ui/ConfirmationModal.svelte"
  import { Plus, Edit, Trash2, Calendar, Play } from "@lucide/svelte"

  let schedules = $state<Schedule[]>([])
  let loading = $state(true)
  let searchTerm = $state("")
  let showModal = $state(false)
  let modalMode = $state<"create" | "edit">("create")
  let selectedSchedule = $state<Schedule | null>(null)
  let saving = $state(false)
  let currentPage = $state(1)
  let totalPages = $state(1)
  let totalItems = $state(0)
  let pageSize = $state(20)

  // Run schedule modal states
  let showRunModal = $state(false)
  let runType = $state<"transfer" | "pull">("transfer")
  let runLoading = $state(false)
  let selectedScheduleId = $state<number | null>(null)

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
    scheduleName: "",
    status: true,
    value: 0,
  })

  let errors = $state<Record<string, string>>({})

  // Mobile-optimized columns
  const mobileColumns = [
    {
      key: "scheduleName",
      label: "Schedule",
      render: (value: string, row: Schedule) => {
        const statusColor = row.status
          ? "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300"
          : "bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300"

        const statusText = row.status ? "Active" : "Inactive"

        return `
          <div class="space-y-3">
            <div class="flex items-start justify-between">
              <div class="min-w-0 flex-1 pr-3">
                <h3 class="font-medium text-gray-900 dark:text-white text-base leading-tight break-words">${value}</h3>
              </div>
              <div class="flex items-center space-x-1 flex-shrink-0">
                <button onclick="editSchedule(${row.id})" class="p-2 text-green-600 hover:text-green-800 dark:text-green-400 dark:hover:text-green-300 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition-colors touch-manipulation min-h-[44px] min-w-[44px] flex items-center justify-center" title="Edit">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
                </button>
                <button onclick="deleteSchedule(${row.id})" class="p-2 text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition-colors touch-manipulation min-h-[44px] min-w-[44px] flex items-center justify-center" title="Delete">
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg>
                </button>
              </div>
            </div>
            
            <div class="flex items-center">
              <div 
                onclick="selectSchedule(${row.id})"
                class="h-4 w-4 rounded-full border-2 cursor-pointer mr-3 flex-shrink-0 transition-colors ${
                  selectedScheduleId === row.id
                    ? "border-supabase-green bg-supabase-green"
                    : "border-gray-300 dark:border-gray-600 hover:border-supabase-green"
                }"
                title="${selectedScheduleId === row.id ? "Selected" : "Click to select"}"
              >
                ${selectedScheduleId === row.id ? '<div class="w-1.5 h-1.5 bg-white rounded-full m-auto mt-0.5"></div>' : ""}
              </div>
              <div class="flex flex-wrap gap-2 text-xs">
                <span class="inline-flex items-center px-2.5 py-0.5 rounded-full font-medium ${statusColor}">
                  ${statusText}
                </span>
                <span class="inline-flex items-center px-2.5 py-0.5 rounded-full font-medium bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300">
                  Value: ${row.value}
                </span>
              </div>
            </div>
            
            <div class="text-xs text-gray-600 dark:text-gray-400">
              <span class="font-medium">ID:</span> ${row.id}
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
      render: (value: any, row: Schedule) => {
        const isSelected = selectedScheduleId === row.id
        return `
          <div class="flex items-center justify-center">
            <div 
              onclick="selectSchedule(${row.id})"
              class="h-4 w-4 rounded-full border-2 cursor-pointer transition-colors ${
                isSelected
                  ? "border-supabase-green bg-supabase-green"
                  : "border-gray-300 dark:border-gray-600 hover:border-supabase-green"
              }"
              title="${isSelected ? "Selected" : "Click to select"}"
            >
              ${isSelected ? '<div class="w-1.5 h-1.5 bg-white rounded-full m-auto mt-0.5"></div>' : ""}
            </div>
          </div>
        `
      },
    },
    { key: "id", label: "ID", sortable: true, width: "80px" },
    { key: "scheduleName", label: "Name", sortable: true },
    {
      key: "status",
      label: "Status",
      render: (value: boolean) => {
        const variant = value ? "success" : "error"
        const text = value ? "Active" : "Inactive"
        const colors = {
          success:
            "bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300",
          error: "bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300",
        }
        return `<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colors[variant]}">${text}</span>`
      },
    },
    { key: "value", label: "Value", sortable: true },
    {
      key: "actions",
      label: "Actions",
      render: (value: any, row: Schedule) => {
        return `
          <div class="flex space-x-2">
            <button onclick="editSchedule(${row.id})" class="text-green-600 hover:text-green-800 dark:text-green-400 dark:hover:text-green-300 p-1" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteSchedule(${row.id})" class="text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300 p-1" title="Delete">
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

  onMount(async () => {
    await loadSchedules()
  })

  async function loadSchedules() {
    try {
      loading = true
      const filters: Record<string, string> = {
        take: pageSize.toString(),
        skip: ((currentPage - 1) * pageSize).toString(),
      }

      if (searchTerm) filters.name = searchTerm

      const response = await api.getSchedules(filters)
      schedules = response.content || []

      // Calculate pagination
      totalItems = response.entityCount || 0
      totalPages = Math.ceil(totalItems / pageSize)
    } catch (error) {
      showToastMessage(
        "Failed to load schedules. Please check your connection and try again.",
        "error",
      )
    } finally {
      loading = false
    }
  }

  function selectSchedule(id: number) {
    selectedScheduleId = selectedScheduleId === id ? null : id
    // Force a re-render to update visual indicators
    schedules = [...schedules]
  }

  function openRunModal() {
    if (!selectedScheduleId) {
      showToastMessage("Please select a schedule to run", "error")
      return
    }
    showRunModal = true
  }

  async function runSchedule() {
    if (!selectedScheduleId) {
      showToastMessage("No schedule selected", "error")
      return
    }

    const schedule = schedules.find((s) => s.id === selectedScheduleId)
    if (!schedule) {
      showToastMessage("Selected schedule not found", "error")
      return
    }

    if (!schedule.status) {
      showToastMessage(
        "Cannot run inactive schedule. Please activate it first.",
        "error",
      )
      return
    }

    runLoading = true
    try {
      // Use the standard executeTransfer/executePull methods with scheduleId
      const apiFilters = {
        scheduleId: selectedScheduleId.toString(),
      }

      let response
      if (runType === "transfer") {
        response = await api.executeTransfer(apiFilters)
      } else {
        response = await api.executePull(apiFilters)
      }

      // Extract job GUID from the response
      let jobGuid = null
      if (response && response.information) {
        jobGuid = response.information
      }

      // Show success message with job GUID
      if (jobGuid) {
        showToastMessage(
          `${runType === "transfer" ? "Transfer" : "Pull"} job started successfully for schedule "${schedule.scheduleName}". Job ID: ${jobGuid}`,
          "success",
        )
      } else {
        showToastMessage(
          `${runType === "transfer" ? "Transfer" : "Pull"} job started successfully for schedule "${schedule.scheduleName}"`,
          "success",
        )
      }

      showRunModal = false
      selectedScheduleId = null
    } catch (error) {
      showToastMessage(
        `Failed to start ${runType} job for schedule: ${error.message}`,
        "error",
      )
    } finally {
      runLoading = false
    }
  }

  function openCreateModal() {
    modalMode = "create"
    selectedSchedule = null
    formData = {
      scheduleName: "",
      status: true,
      value: 0,
    }
    errors = {}
    showModal = true
  }

  function openEditModal(schedule: Schedule) {
    modalMode = "edit"
    selectedSchedule = schedule
    formData = {
      scheduleName: schedule.scheduleName,
      status: schedule.status,
      value: schedule.value,
    }
    errors = {}
    showModal = true
  }

  function validateForm(): boolean {
    errors = {}

    if (!formData.scheduleName.trim()) {
      errors.scheduleName = "Name is required"
    }

    if (formData.value < 0) {
      errors.value = "Value must be non-negative"
    }

    return Object.keys(errors).length === 0
  }

  async function handleSubmit() {
    if (!validateForm()) return

    try {
      saving = true

      const scheduleData = {
        scheduleName: formData.scheduleName,
        status: formData.status,
        value: formData.value,
      }

      if (modalMode === "create") {
        await api.createSchedule(scheduleData)
        showToastMessage("Schedule created successfully", "success")
      } else if (selectedSchedule) {
        await api.updateSchedule(selectedSchedule.id, scheduleData)
        showToastMessage("Schedule updated successfully", "success")
      }

      showModal = false
      await loadSchedules()
    } catch (error) {
      showToastMessage(
        `Failed to ${modalMode} schedule: ${error.message}`,
        "error",
      )
    } finally {
      saving = false
    }
  }

  function handlePageChange(page: number) {
    currentPage = page
    selectedScheduleId = null // Clear selection when changing pages
    loadSchedules()
  }

  function handlePageSizeChange(newPageSize: number) {
    pageSize = newPageSize
    currentPage = 1
    selectedScheduleId = null
    loadSchedules()
  }

  function showDeleteConfirmation(id: number) {
    const schedule = schedules.find((s) => s.id === id)
    confirmTitle = "Delete Schedule"
    confirmMessage = `Are you sure you want to delete "${schedule?.scheduleName || "this schedule"}"? This action cannot be undone and may affect extractions using this schedule.`
    confirmAction = async () => {
      confirmLoading = true
      try {
        const response = await api.deleteSchedule(id)

        // Handle 204 No Content response (successful deletion)
        if (response?.statusCode === 204 || !response?.error) {
          // Clear selection if deleted schedule was selected
          if (selectedScheduleId === id) {
            selectedScheduleId = null
          }

          await loadSchedules()
          showToastMessage(
            `Schedule "${schedule?.scheduleName || `ID: ${id}`}" deleted successfully`,
            "success",
          )
        } else {
          throw new Error(
            response?.information || "Unexpected response from server",
          )
        }
      } catch (error) {
        let errorMessage = "Failed to delete schedule"
        if (error instanceof Error) {
          if (error.message.includes("404")) {
            errorMessage =
              "Schedule not found - it may have already been deleted"
          } else if (error.message.includes("409")) {
            errorMessage =
              "Cannot delete schedule - it is being used by one or more extractions"
          } else {
            errorMessage = `Failed to delete schedule: ${error.message}`
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
    ;(window as any).editSchedule = (id: number) => {
      const schedule = schedules.find((s) => s.id === id)
      if (schedule) openEditModal(schedule)
    }
    ;(window as any).deleteSchedule = (id: number) => {
      showDeleteConfirmation(id)
    }
    ;(window as any).selectSchedule = (id: number) => {
      selectSchedule(id)
    }
  }

  $effect(() => {
    loadSchedules()
  })
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
      <div class="flex flex-col sm:flex-row gap-2 sm:gap-3">
        <Button
          variant="secondary"
          onclick={openRunModal}
          disabled={!selectedScheduleId}
        >
          <Play size={16} class="mr-2" />
          Run Schedule
          {#if selectedScheduleId}
            ({schedules.find((s) => s.id === selectedScheduleId)?.scheduleName})
          {/if}
        </Button>
        <Button variant="primary" onclick={openCreateModal}>
          <Plus size={16} class="mr-2" />
          New Schedule
        </Button>
      </div>
    {/snippet}
  </PageHeader>

  <!-- Search -->
  <div
    class="bg-white dark:bg-gray-800 p-4 rounded-lg shadow-sm border border-gray-200 dark:border-gray-700"
  >
    <div
      class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-4"
    >
      <div class="flex-1 max-w-md">
        <Input placeholder="Search schedules..." bind:value={searchTerm} />
      </div>
      <div class="text-sm text-gray-600 dark:text-gray-400">
        Showing {schedules.length} of {totalItems.toLocaleString()} schedules
        {#if selectedScheduleId}
          • 1 selected
        {/if}
      </div>
    </div>
  </div>

  <!-- Schedules Content -->
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
                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 714 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
              ></path>
            </svg>
          </div>
        {:else if schedules.length === 0}
          <div class="text-center py-8">
            <Calendar
              class="mx-auto h-12 w-12 text-gray-400 dark:text-gray-500"
            />
            <h3 class="mt-2 text-sm font-medium text-gray-900 dark:text-white">
              No schedules found
            </h3>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
              Get started by creating a new schedule.
            </p>
          </div>
        {:else}
          {#each schedules as schedule}
            <div
              class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 border border-gray-200 dark:border-gray-600 hover:bg-gray-100 dark:hover:bg-gray-600/50 transition-colors"
            >
              {@html mobileColumns[0].render(schedule.scheduleName, schedule)}
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
          data={schedules}
          {loading}
          emptyMessage="No schedules found"
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

<!-- Run Schedule Modal -->
<Modal bind:open={showRunModal} title="Run Schedule" size="md">
  <div class="space-y-6">
    {#if selectedScheduleId}
      {@const schedule = schedules.find((s) => s.id === selectedScheduleId)}

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
              Run Schedule: "{schedule?.scheduleName}"
            </h3>
            <div class="mt-2 text-sm text-blue-700 dark:text-blue-400">
              <p>
                This will execute all extractions associated with this schedule.
                You can monitor the job progress in the Jobs section.
              </p>
            </div>
          </div>
        </div>
      </div>

      <div
        class="bg-gray-50 dark:bg-gray-800 p-4 rounded-md border border-gray-200 dark:border-gray-700"
      >
        <h5 class="text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">
          Schedule Details:
        </h5>
        <div class="space-y-3 text-sm">
          <div class="flex justify-between">
            <span class="text-gray-600 dark:text-gray-400">Schedule Name:</span>
            <span class="font-medium text-gray-900 dark:text-white"
              >{schedule?.scheduleName}</span
            >
          </div>
          <div class="flex justify-between">
            <span class="text-gray-600 dark:text-gray-400">Status:</span>
            <span class="text-gray-900 dark:text-white">
              {#if schedule?.status}
                <span
                  class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300"
                >
                  Active
                </span>
              {:else}
                <span
                  class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300"
                >
                  Inactive
                </span>
              {/if}
            </span>
          </div>
          <div class="flex justify-between">
            <span class="text-gray-600 dark:text-gray-400">Value:</span>
            <span class="font-medium text-gray-900 dark:text-white"
              >{schedule?.value}</span
            >
          </div>
        </div>
      </div>

      <div class="space-y-4">
        <Select
          label="Execution Type"
          bind:value={runType}
          options={[
            { value: "transfer", label: "Transfer (to destination databases)" },
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
                {#if runType === "transfer"}
                  <strong>Transfer mode:</strong> All extractions in this schedule
                  will transfer data to their configured destinations.
                {:else}
                  <strong>Pull mode:</strong> All extractions in this schedule will
                  extract data and make it available as CSV files.
                {/if}
              </p>
            </div>
          </div>
        </div>

        {#if !schedule?.status}
          <div
            class="bg-red-50 dark:bg-red-900/20 border-l-4 border-red-400 dark:border-red-600 p-3"
          >
            <div class="flex">
              <div class="flex-shrink-0">
                <svg
                  class="h-5 w-5 text-red-400 dark:text-red-300"
                  fill="currentColor"
                  viewBox="0 0 20 20"
                >
                  <path
                    fill-rule="evenodd"
                    d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
                    clip-rule="evenodd"
                  />
                </svg>
              </div>
              <div class="ml-3">
                <p class="text-sm text-red-700 dark:text-red-400">
                  <strong>Warning:</strong> This schedule is currently inactive.
                  Please activate it before running.
                </p>
              </div>
            </div>
          </div>
        {/if}
      </div>

      <div
        class="flex flex-col sm:flex-row justify-end space-y-3 sm:space-y-0 sm:space-x-3 pt-4 border-t border-gray-200 dark:border-gray-700"
      >
        <Button
          variant="secondary"
          onclick={() => (showRunModal = false)}
          disabled={runLoading}
        >
          Cancel
        </Button>
        <Button
          variant="primary"
          loading={runLoading}
          onclick={runSchedule}
          disabled={runLoading || !schedule?.status}
        >
          {#if runLoading}
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
                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 714 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
              ></path>
            </svg>
            Starting {runType}...
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
            Run {runType}
          {/if}
        </Button>
      </div>
    {/if}
  </div>
</Modal>

<!-- Create/Edit Modal -->
<Modal
  bind:open={showModal}
  title={modalMode === "create" ? "New Schedule" : "Edit Schedule"}
>
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
          class="rounded border-gray-300 dark:border-gray-600 text-supabase-green focus:ring-supabase-green dark:bg-gray-800"
        />
        <span class="text-sm font-medium text-gray-700 dark:text-gray-300"
          >Active</span
        >
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

    <div
      class="flex flex-col sm:flex-row justify-end space-y-3 sm:space-y-0 sm:space-x-3 pt-4"
    >
      <Button variant="secondary" onclick={() => (showModal = false)}>
        Cancel
      </Button>
      <Button variant="primary" type="submit" loading={saving}>
        {modalMode === "create" ? "Create" : "Save"} Schedule
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
  confirmText="Delete Schedule"
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

  :global(.job-guid-highlight:hover) {
    background-color: #fed7aa;
    border-color: #f97316;
    transform: scale(1.02);
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

    /* Touch-friendly radio button sizing */
    :global(input[type="radio"]) {
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
