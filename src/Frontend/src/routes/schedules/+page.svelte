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
  import { Plus, Edit, Trash2, Calendar, Play, RefreshCw } from "@lucide/svelte"

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

  const columns = [
    {
      key: "selection",
      label: "",
      width: "40px",
      render: (value: any, row: Schedule) => {
        const isSelected = selectedScheduleId === row.id
        return `
          <input 
            type="radio" 
            name="selectedSchedule"
            ${isSelected ? "checked" : ""} 
            onchange="selectSchedule(${row.id})"
            class="border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
          />
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
          success: "bg-green-100 text-green-800",
          error: "bg-red-100 text-red-800",
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
            <button onclick="editSchedule(${row.id})" class="text-green-600 hover:text-green-800" title="Edit">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path></svg>
            </button>
            <button onclick="deleteSchedule(${row.id})" class="text-red-600 hover:text-red-800" title="Delete">
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
    const jobGuidPattern = /Job ID: ([a-f0-9-]{36})/i;
    const match = message.match(jobGuidPattern);
    
    if (match && type === "success") {
      // Format the message with highlighted job GUID
      const jobGuid = match[1];
      const baseMessage = message.replace(jobGuidPattern, "");
      
      toastMessage = `<div class="toast-content">${baseMessage}<br><span class="job-guid-highlight">Job ID: ${jobGuid}</span><br><small>Click to copy job ID</small></div>`;
      
      // Add click handler to copy job GUID
      setTimeout(() => {
        const toastElement = document.querySelector('.job-guid-highlight');
        if (toastElement) {
          toastElement.onclick = () => {
            navigator.clipboard.writeText(jobGuid).then(() => {
              console.log('Job GUID copied to clipboard:', jobGuid);
              // Show a brief confirmation
              const originalText = toastElement.textContent;
              toastElement.textContent = 'Copied!';
              setTimeout(() => {
                toastElement.textContent = originalText;
              }, 1000);
            }).catch(err => {
              console.error('Failed to copy job GUID:', err);
            });
          };
        }
      }, 100);
      
      // Use HTML mode for toast
      showToast = true;
      return;
    }
    
    // Regular message without HTML
    toastMessage = message;
    toastType = type;
    showToast = true;
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
      console.error("Failed to load schedules:", error)
      showToastMessage(
        "Failed to load schedules. Please check your connection and try again.",
        "error",
      )
    } finally {
      loading = false
    }
  }

  async function refreshData() {
    await loadSchedules()
    showToastMessage("Data refreshed successfully", "success")
  }

  function selectSchedule(id: number) {
    selectedScheduleId = selectedScheduleId === id ? null : id
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

    const schedule = schedules.find(s => s.id === selectedScheduleId)
    if (!schedule) {
      showToastMessage("Selected schedule not found", "error")
      return
    }

    if (!schedule.status) {
      showToastMessage("Cannot run inactive schedule. Please activate it first.", "error")
      return
    }

    runLoading = true
    try {
      // Use the standard executeTransfer/executePull methods with scheduleId
      const apiFilters = {
        scheduleId: selectedScheduleId.toString(),
      }

      console.log("Running schedule with filters:", apiFilters)

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
      console.error(`Failed to run ${runType} for schedule:`, error)
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
      console.error(`Failed to ${modalMode} schedule:`, error)
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
            "success"
          )
        } else {
          throw new Error(response?.information || "Unexpected response from server")
        }
      } catch (error) {
        console.error("Failed to delete schedule:", error)
        
        let errorMessage = "Failed to delete schedule"
        if (error instanceof Error) {
          if (error.message.includes('404')) {
            errorMessage = "Schedule not found - it may have already been deleted"
          } else if (error.message.includes('409')) {
            errorMessage = "Cannot delete schedule - it is being used by one or more extractions"
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
      <div class="flex space-x-3">
        <Button variant="ghost" onclick={refreshData} {loading}>
          <RefreshCw size={16} class="mr-2" />
          Refresh
        </Button>
        <Button
          variant="secondary"
          onclick={openRunModal}
          disabled={!selectedScheduleId}
        >
          <Play size={16} class="mr-2" />
          Run Schedule
          {#if selectedScheduleId}
            ({schedules.find(s => s.id === selectedScheduleId)?.scheduleName})
          {/if}
        </Button>
        <Button variant="primary" onclick={openCreateModal}>
          <Plus size={16} class="mr-2" />
          New Schedule
        </Button>
      </div>
    {/snippet}
  </PageHeader>

  <!-- Filters -->
  <div class="bg-white p-4 rounded-lg shadow">
    <div class="flex justify-between items-center gap-4">
      <div class="max-w-md flex-1">
        <Input placeholder="Search schedules..." bind:value={searchTerm} />
      </div>
      <div class="text-sm text-supabase-gray-600">
        Showing {schedules.length} of {totalItems.toLocaleString()} schedules
        {#if selectedScheduleId}
          • 1 selected
        {/if}
      </div>
    </div>
  </div>

  <!-- Schedules Table -->
  <div class="bg-white shadow rounded-lg">
    <div class="p-6">
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

<!-- Run Schedule Modal -->
<Modal bind:open={showRunModal} title="Run Schedule" size="md">
  <div class="space-y-6">
    {#if selectedScheduleId}
      {@const schedule = schedules.find(s => s.id === selectedScheduleId)}
      
      <div class="bg-blue-50 border-l-4 border-blue-400 p-4">
        <div class="flex">
          <div class="flex-shrink-0">
            <svg class="h-5 w-5 text-blue-400" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
            </svg>
          </div>
          <div class="ml-3">
            <h3 class="text-sm font-medium text-blue-800">
              Run Schedule: "{schedule?.scheduleName}"
            </h3>
            <div class="mt-2 text-sm text-blue-700">
              <p>This will execute all extractions associated with this schedule. You can monitor the job progress in the Jobs section.</p>
            </div>
          </div>
        </div>
      </div>

      <div class="bg-supabase-gray-50 p-4 rounded-md">
        <h5 class="text-sm font-medium text-supabase-gray-700 mb-3">
          Schedule Details:
        </h5>
        <div class="space-y-3 text-sm">
          <div class="flex justify-between">
            <span class="text-supabase-gray-600">Schedule Name:</span>
            <span class="font-medium text-supabase-gray-900">{schedule?.scheduleName}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-supabase-gray-600">Status:</span>
            <span class="text-supabase-gray-900">
              {#if schedule?.status}
                <span class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-green-100 text-green-800">
                  Active
                </span>
              {:else}
                <span class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-red-100 text-red-800">
                  Inactive
                </span>
              {/if}
            </span>
          </div>
          <div class="flex justify-between">
            <span class="text-supabase-gray-600">Value:</span>
            <span class="font-medium text-supabase-gray-900">{schedule?.value}</span>
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

        <div class="bg-yellow-50 border-l-4 border-yellow-400 p-3">
          <div class="flex">
            <div class="flex-shrink-0">
              <svg class="h-5 w-5 text-yellow-400" fill="currentColor" viewBox="0 0 20 20">
                <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
              </svg>
            </div>
            <div class="ml-3">
              <p class="text-sm text-yellow-700">
                {#if runType === "transfer"}
                  <strong>Transfer mode:</strong> All extractions in this schedule will transfer data to their configured destinations.
                {:else}
                  <strong>Pull mode:</strong> All extractions in this schedule will extract data and make it available as CSV files.
                {/if}
              </p>
            </div>
          </div>
        </div>

        {#if !schedule?.status}
          <div class="bg-red-50 border-l-4 border-red-400 p-3">
            <div class="flex">
              <div class="flex-shrink-0">
                <svg class="h-5 w-5 text-red-400" fill="currentColor" viewBox="0 0 20 20">
                  <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
                </svg>
              </div>
              <div class="ml-3">
                <p class="text-sm text-red-700">
                  <strong>Warning:</strong> This schedule is currently inactive. Please activate it before running.
                </p>
              </div>
            </div>
          </div>
        {/if}
      </div>

      <div class="flex justify-end space-x-3 pt-4 border-t border-supabase-gray-200">
        <Button variant="secondary" onclick={() => (showRunModal = false)} disabled={runLoading}>
          Cancel
        </Button>
        <Button
          variant="primary"
          loading={runLoading}
          onclick={runSchedule}
          disabled={runLoading || !schedule?.status}
        >
          {#if runLoading}
            <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
            </svg>
            Starting {runType}...
          {:else}
            <svg class="w-4 h-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M14.828 14.828a4 4 0 01-5.656 0M9 10h1m4 0h1m-6 4h8m-9-4V8a3 3 0 016 0v2M5 12h14l-1 7H6l-1-7z"></path>
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
          class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
        />
        <span class="text-sm font-medium text-supabase-gray-700">Active</span>
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

    <div class="flex justify-end space-x-3 pt-4">
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
<Toast bind:show={showToast} type={toastType} message={toastMessage} allowHtml={true} />

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
</style>