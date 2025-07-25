<script lang="ts">
  import { page } from "$app/stores"
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import type { Extraction } from "$lib/types.js"
  import PageHeader from "$lib/components/layout/PageHeader.svelte"
  import Card from "$lib/components/ui/Card.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Badge from "$lib/components/ui/Badge.svelte"
  import Table from "$lib/components/ui/Table.svelte"
  import Modal from "$lib/components/ui/Modal.svelte"
  import Toast from "$lib/components/ui/Toast.svelte"
  import ConfirmationModal from "$lib/components/ui/ConfirmationModal.svelte"
  import {
    Edit,
    Play,
    Download,
    Eye,
    ArrowLeft,
    FileDown,
  } from "@lucide/svelte"
  import { goto } from "$app/navigation"
  import { browser } from "$app/environment"
  import { themeStore } from "$lib/stores/theme.svelte.js"

  let extraction = $state<Extraction | null>(null)
  let loading = $state(true)
  let previewData = $state<any[]>([])
  let previewLoading = $state(false)
  let showPreviewModal = $state(false)
  let previewColumns = $state<any[]>([])
  let executeLoading = $state(false)

  // Execute confirmation modal state
  let showExecuteModal = $state(false)
  let executeType = $state<"transfer" | "pull">("transfer")
  let executeConfirmLoading = $state(false)
  let overrideFilterTime = $state("")

  // Toast notifications
  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  const extractionId = $derived(+$page.params.id)

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

      toastMessage = `${baseMessage}<br><span class="job-guid-highlight">Job ID: ${jobGuid}</span><br><small>Click to copy job ID</small>`

      // Add click handler to copy job GUID
      setTimeout(() => {
        const toastElement = document.querySelector(".job-guid-highlight")
        if (toastElement) {
          toastElement.style.cursor = "pointer"
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
    } else {
      toastMessage = message
    }

    toastType = type
    showToast = true
  }

  onMount(async () => {
    await loadExtraction()
  })

  async function loadExtraction() {
    try {
      loading = true
      const response = await api.getExtraction(extractionId)
      extraction = response.content?.[0] || null
    } catch (error) {
      console.error("Failed to load extraction:", error)
      showToastMessage("Failed to load extraction details", "error")
    } finally {
      loading = false
    }
  }

  async function fetchPreview() {
    if (!extraction) return

    try {
      previewLoading = true

      const response = await api.fetchData({
        name: extraction.extractionName,
        page: "1",
      })

      // Handle successful response (200 or 204)
      if (
        !response.error &&
        (response.statusCode === 200 || response.statusCode === 204)
      ) {
        previewData = response.content || []

        if (previewData.length > 0) {
          previewColumns = Object.keys(previewData[0]).map((key) => ({
            key,
            label: key,
            sortable: false,
            width: "150px", // Fixed width for better scrolling
          }))

          showPreviewModal = true
          showToastMessage(
            `Loaded ${previewData.length} rows for preview`,
            "success",
          )
        } else {
          showToastMessage("No data returned from the source", "info")
        }
      } else {
        // Handle API error response
        const errorMessage = response.information || "Unknown error occurred"
        showToastMessage(
          `Failed to fetch preview data: ${errorMessage}`,
          "error",
        )
      }
    } catch (error) {
      console.error("Failed to fetch preview:", error)

      // Improved error message handling
      let errorMessage = "Failed to fetch preview data"

      if (error instanceof Error) {
        errorMessage = error.message
      } else if (typeof error === "string") {
        errorMessage = error
      } else if (error && typeof error === "object") {
        if ("message" in error) {
          errorMessage = error.message
        } else if ("information" in error) {
          errorMessage = error.information
        } else {
          errorMessage = "An unexpected error occurred"
        }
      }

      showToastMessage(errorMessage, "error")
    } finally {
      previewLoading = false
    }
  }

  function openExecuteModal(type: "transfer" | "pull") {
    executeType = type
    showExecuteModal = true
  }

  async function executeJob() {
    if (!extraction) return

    // Validate execution requirements
    if (executeType === "transfer" && !extraction.destinationId) {
      showToastMessage(
        "Cannot execute transfer: No destination configured for this extraction",
        "error",
      )
      return
    }

    if (!extraction.originId) {
      showToastMessage(
        "Cannot execute: No origin configured for this extraction",
        "error",
      )
      return
    }

    try {
      executeConfirmLoading = true
      const apiFilters = { name: extraction.extractionName }

      // Add override filter time if specified
      if (overrideFilterTime && !isNaN(+overrideFilterTime) && +overrideFilterTime > 0) {
        apiFilters.overrideTime = overrideFilterTime
      }

      let response
      if (executeType === "transfer") {
        response = await api.executeTransfer(apiFilters)
      } else {
        response = await api.executePull(apiFilters)
      }

      if (
        !response.error &&
        (response.statusCode === 200 || response.statusCode === 204)
      ) {
        // Extract job GUID from the response
        let jobGuid = null
        if (response && response.information) {
          jobGuid = response.information
        }

        // Build success message with override info
        let successMessage = `${executeType === "transfer" ? "Transfer" : "Pull"} job started successfully for "${extraction.extractionName}"`
        
        if (overrideFilterTime && !isNaN(+overrideFilterTime) && +overrideFilterTime > 0) {
          successMessage += ` with override filter time of ${overrideFilterTime} seconds`
        }
        
        if (jobGuid) {
          successMessage += `. Job ID: ${jobGuid}`
        }

        showToastMessage(successMessage, "success")

        showExecuteModal = false
        overrideFilterTime = "" // Reset override filter time
      } else {
        // Handle API error response
        const errorMessage = response.information || "Unknown error occurred"
        showToastMessage(
          `Failed to start ${executeType} job: ${errorMessage}`,
          "error",
        )
      }
    } catch (error) {
      console.error(`Failed to execute ${executeType} job:`, error)

      // Improved error message handling
      let errorMessage = `Failed to start ${executeType} job`

      if (error instanceof Error) {
        errorMessage = error.message
      } else if (typeof error === "string") {
        errorMessage = error
      } else if (error && typeof error === "object") {
        if ("message" in error) {
          errorMessage = error.message
        } else if ("information" in error) {
          errorMessage = error.information
        } else {
          errorMessage = "An unexpected error occurred"
        }
      }

      showToastMessage(errorMessage, "error")
    } finally {
      executeConfirmLoading = false
    }
  }

  function exportToCSV() {
    if (previewData.length === 0) {
      showToastMessage("No data available to export", "error")
      return
    }

    try {
      // Create CSV content
      const headers = previewColumns.map((col) => col.label)
      const csvContent = [
        headers.join(","),
        ...previewData.map((row) =>
          headers
            .map((header) => {
              const value = row[header]
              // Escape values that contain commas, quotes, or newlines
              if (
                typeof value === "string" &&
                (value.includes(",") ||
                  value.includes('"') ||
                  value.includes("\n"))
              ) {
                return `"${value.replace(/"/g, '""')}"`
              }
              return value ?? ""
            })
            .join(","),
        ),
      ].join("\n")

      // Create and download file
      const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" })
      const link = document.createElement("a")
      const url = URL.createObjectURL(blob)
      link.setAttribute("href", url)
      link.setAttribute(
        "download",
        `${extraction?.extractionName || "extraction"}_preview.csv`,
      )
      link.style.visibility = "hidden"
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
      URL.revokeObjectURL(url)

      showToastMessage(
        `CSV exported successfully (${previewData.length} rows)`,
        "success",
      )
    } catch (error) {
      console.error("Failed to export CSV:", error)
      showToastMessage("Failed to export CSV file", "error")
    }
  }
  
  function handleBackNavigation() {
    if (browser) {
      themeStore.applyTheme()
      
      history.back()
    }
  }

  function exportToJSON() {
    if (previewData.length === 0) {
      showToastMessage("No data available to export", "error")
      return
    }

    try {
      const jsonContent = JSON.stringify(previewData, null, 2)
      const blob = new Blob([jsonContent], {
        type: "application/json;charset=utf-8;",
      })
      const link = document.createElement("a")
      const url = URL.createObjectURL(blob)
      link.setAttribute("href", url)
      link.setAttribute(
        "download",
        `${extraction?.extractionName || "extraction"}_preview.json`,
      )
      link.style.visibility = "hidden"
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
      URL.revokeObjectURL(url)

      showToastMessage(
        `JSON exported successfully (${previewData.length} rows)`,
        "success",
      )
    } catch (error) {
      console.error("Failed to export JSON:", error)
      showToastMessage("Failed to export JSON file", "error")
    }
  }
</script>

<svelte:head>
  <title>{extraction?.extractionName || "Loading..."} - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader
    title={extraction?.extractionName || "Loading..."}
    description="Extraction configuration details"
  >
    {#snippet actions()}
      <div class="flex flex-col sm:flex-row gap-2 sm:gap-3">
        <Button variant="ghost" onclick={handleBackNavigation}>
          <ArrowLeft size={16} class="mr-2" />
          Back
        </Button>
        {#if extraction}
          <Button
            variant="secondary"
            onclick={fetchPreview}
            loading={previewLoading}
          >
            <Eye size={16} class="mr-2" />
            Preview Data
          </Button>
          <Button
            variant="secondary"
            onclick={() => openExecuteModal("pull")}
            loading={executeLoading}
          >
            <Download size={16} class="mr-2" />
            Pull to CSV
          </Button>
          <Button
            variant="primary"
            onclick={() => openExecuteModal("transfer")}
            loading={executeLoading}
          >
            <Play size={16} class="mr-2" />
            Transfer to Destination
          </Button>
          <Button
            variant="secondary"
            onclick={() =>
              (window.location.href = `/extractions/${extractionId}/edit`)}
          >
            <Edit size={16} class="mr-2" />
            Edit
          </Button>
        {/if}
      </div>
    {/snippet}
  </PageHeader>

  {#if loading}
    <div class="flex justify-center py-12">
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
  {:else if !extraction}
    <Card>
      <div class="text-center py-12">
        <p class="text-gray-500 dark:text-gray-400">Extraction not found</p>
      </div>
    </Card>
  {:else}
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <!-- Main Info -->
      <div class="lg:col-span-2 space-y-6">
        <Card title="Basic Information">
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Name</span
              >
              <p class="mt-1 text-sm text-gray-900 dark:text-white">
                {extraction.extractionName}
              </p>
            </div>
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Alias</span
              >
              <p class="mt-1 text-sm text-gray-900 dark:text-white">
                {extraction.extractionAlias || "-"}
              </p>
            </div>
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Source Type</span
              >
              <p class="mt-1">
                <Badge
                  variant={extraction.sourceType === "http"
                    ? "info"
                    : "success"}
                >
                  {extraction.sourceType || "db"}
                </Badge>
              </p>
            </div>
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Index Name</span
              >
              <p class="mt-1 text-sm text-gray-900 dark:text-white">
                {extraction.indexName || "-"}
              </p>
            </div>
          </div>
        </Card>

        <Card title="Configuration">
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Incremental</span
              >
              <p class="mt-1">
                <Badge
                  variant={extraction.isIncremental ? "success" : "default"}
                >
                  {extraction.isIncremental ? "Yes" : "No"}
                </Badge>
              </p>
            </div>
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Virtual</span
              >
              <p class="mt-1">
                <Badge variant={extraction.isVirtual ? "success" : "default"}>
                  {extraction.isVirtual ? "Yes" : "No"}
                </Badge>
              </p>
            </div>
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Filter Time</span
              >
              <p class="mt-1 text-sm text-gray-900 dark:text-white">
                {extraction.filterTime || "-"}
              </p>
            </div>
          </div>
        </Card>

        {#if extraction.sourceType === "http"}
          <Card title="HTTP Configuration">
            <div class="space-y-4">
              <div>
                <span
                  class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                  >Endpoint</span
                >
                <p class="mt-1 text-sm text-gray-900 dark:text-white break-all">
                  {extraction.endpointFullName || "-"}
                </p>
              </div>
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <span
                    class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                    >HTTP Method</span
                  >
                  <p class="mt-1">
                    <Badge variant="info"
                      >{extraction.httpMethod || "GET"}</Badge
                    >
                  </p>
                </div>
                <div>
                  <span
                    class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                    >Pagination Type</span
                  >
                  <p class="mt-1 text-sm text-gray-900 dark:text-white">
                    {extraction.paginationType || "-"}
                  </p>
                </div>
              </div>
            </div>
          </Card>
        {/if}

        {#if extraction.overrideQuery}
          <Card title="Override Query">
            <pre
              class="text-sm text-gray-900 dark:text-white bg-gray-50 dark:bg-gray-700 p-4 rounded-md overflow-x-auto">{extraction.overrideQuery}</pre>
          </Card>
        {/if}

        {#if extraction.script}
          <Card title="Custom Script">
            <pre
              class="text-sm text-gray-900 dark:text-white bg-gray-50 dark:bg-gray-700 p-4 rounded-md overflow-x-auto">{extraction.script}</pre>
          </Card>
        {/if}
      </div>

      <!-- Relations and Actions -->
      <div class="space-y-6">
        <Card title="Relations">
          <div class="space-y-4">
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Origin</span
              >
              <p class="mt-1 text-sm text-gray-900 dark:text-white">
                {extraction.origin?.originName || "-"}
              </p>
              {#if !extraction.originId}
                <p class="mt-1 text-xs text-red-600 dark:text-red-400">
                  ⚠ No origin configured
                </p>
              {/if}
            </div>
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Destination</span
              >
              <p class="mt-1 text-sm text-gray-900 dark:text-white">
                {extraction.destination?.destinationName || "-"}
              </p>
              {#if !extraction.destinationId}
                <p class="mt-1 text-xs text-orange-600 dark:text-orange-400">
                  ⚠ No destination configured (Pull only)
                </p>
              {/if}
            </div>
            <div>
              <span
                class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                >Schedule</span
              >
              <p class="mt-1 text-sm text-gray-900 dark:text-white">
                {extraction.schedule?.scheduleName || "-"}
              </p>
            </div>
          </div>
        </Card>

        <!-- Quick Actions Card -->
        <Card title="Quick Actions">
          {#if !extraction.originId || !extraction.destinationId}
            <div
              class="mt-4 p-3 bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-800 rounded-md"
            >
              <h4
                class="text-sm font-medium text-yellow-800 dark:text-yellow-300"
              >
                Configuration Required
              </h4>
              <ul
                class="mt-2 text-xs text-yellow-700 dark:text-yellow-400 space-y-1"
              >
                {#if !extraction.originId}
                  <li>• Configure an origin to enable data operations</li>
                {/if}
                {#if !extraction.destinationId}
                  <li>
                    • Configure a destination to enable transfer operations
                  </li>
                {/if}
              </ul>
            </div>
          {/if}
        </Card>

        {#if extraction.filterColumn}
          <Card title="Filtering">
            <div class="space-y-3">
              <div>
                <span
                  class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                  >Filter Column</span
                >
                <p class="mt-1 text-sm text-gray-900 dark:text-white">
                  {extraction.filterColumn}
                </p>
              </div>
              {#if extraction.filterCondition}
                <div>
                  <span
                    class="block text-sm font-medium text-gray-700 dark:text-gray-300"
                    >Filter Condition</span
                  >
                  <p class="mt-1 text-sm text-gray-900 dark:text-white">
                    {extraction.filterCondition}
                  </p>
                </div>
              {/if}
            </div>
          </Card>
        {/if}

        {#if extraction.dependencies}
          <Card title="Dependencies">
            <div class="space-y-2">
              {#each extraction.dependencies.split(",") as dep}
                <span
                  class="inline-block px-2 py-1 text-xs bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200 rounded"
                >
                  {dep.trim()}
                </span>
              {/each}
            </div>
          </Card>
        {/if}
      </div>
    </div>
  {/if}
</div>

<!-- Execute Confirmation Modal -->
<Modal bind:open={showExecuteModal} title="Execute Extraction" size="md">
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
            Execute {executeType === "transfer" ? "Transfer" : "Pull"} Job
          </h3>
          <div class="mt-2 text-sm text-blue-700 dark:text-blue-400">
            <p>
              This will start a background job for "{extraction?.extractionName}".
              You can monitor the job progress in the Jobs section.
            </p>
          </div>
        </div>
      </div>
    </div>

    <!-- Override Filter Time Input -->
    <div>
      <label for="overrideFilterTime" class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
        Override Filter Time (seconds)
      </label>
      <input
        id="overrideFilterTime"
        type="number"
        bind:value={overrideFilterTime}
        placeholder="Optional - override extraction filter time"
        class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-supabase-green focus:border-supabase-green dark:bg-gray-800 dark:text-white"
      />
      <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
        Leave empty to use the extraction's default filter time ({extraction?.filterTime || 'none'} seconds). 
        Specify a value to override for this execution.
      </p>
    </div>

    <div class="bg-gray-50 dark:bg-gray-800 p-4 rounded-md">
      <h5 class="text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">
        Execution Details:
      </h5>
      <div class="space-y-3 text-sm">
        <div class="flex justify-between">
          <span class="text-gray-600 dark:text-gray-400">Extraction:</span>
          <span class="font-medium text-gray-900 dark:text-white"
            >{extraction?.extractionName}</span
          >
        </div>
        <div class="flex justify-between">
          <span class="text-gray-600 dark:text-gray-400">Source:</span>
          <span class="text-gray-900 dark:text-white"
            >{extraction?.origin?.originName || "Not configured"}</span
          >
        </div>
        <div class="flex justify-between">
          <span class="text-gray-600 dark:text-gray-400">Target:</span>
          <span class="text-gray-900 dark:text-white">
            {#if executeType === "transfer"}
              {extraction?.destination?.destinationName || "Not configured"}
            {:else}
              CSV File Download
            {/if}
          </span>
        </div>
        <div class="flex justify-between">
          <span class="text-gray-600 dark:text-gray-400">Type:</span>
          <span class="text-gray-900 dark:text-white">
            <Badge
              variant={extraction?.sourceType === "http" ? "info" : "success"}
            >
              {extraction?.sourceType || "db"}
            </Badge>
          </span>
        </div>
        <div class="flex justify-between">
          <span class="text-gray-600 dark:text-gray-400">Incremental:</span>
          <span class="text-gray-900 dark:text-white">
            <Badge variant={extraction?.isIncremental ? "success" : "default"}>
              {extraction?.isIncremental ? "Yes" : "No"}
            </Badge>
          </span>
        </div>
        <div class="flex justify-between">
          <span class="text-gray-600 dark:text-gray-400">Filter Time:</span>
          <span class="text-gray-900 dark:text-white">
            {#if overrideFilterTime && !isNaN(+overrideFilterTime) && +overrideFilterTime > 0}
              <span class="text-orange-600 dark:text-orange-400 font-medium">
                {overrideFilterTime}s (override)
              </span>
            {:else}
              {extraction?.filterTime || 'Default'}s
            {/if}
          </span>
        </div>
      </div>
    </div>

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
              <strong>Transfer mode:</strong> Data will be transferred from the source
              to the configured destination database.
            {:else}
              <strong>Pull mode:</strong> Data will be extracted from the source
              and made available as CSV files for download.
            {/if}
            {#if overrideFilterTime && !isNaN(+overrideFilterTime) && +overrideFilterTime > 0}
              <br><strong>Filter Override:</strong> Using {overrideFilterTime} seconds instead of the default filter time.
            {/if}
          </p>
        </div>
      </div>
    </div>

    <div
      class="flex flex-col sm:flex-row justify-end space-y-3 sm:space-y-0 sm:space-x-3 pt-4 border-t border-gray-200 dark:border-gray-700"
    >
      <Button
        variant="secondary"
        onclick={() => (showExecuteModal = false)}
        disabled={executeConfirmLoading}
      >
        Cancel
      </Button>
      <Button
        variant="primary"
        loading={executeConfirmLoading}
        onclick={executeJob}
        disabled={executeConfirmLoading ||
          (executeType === "transfer" && !extraction.destinationId)}
      >
        {#if executeConfirmLoading}
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

<!-- Enhanced Preview Modal -->
<Modal
  bind:open={showPreviewModal}
  title="Data Preview"
  size="2xl"
  scrollable={true}
>
  <div class="space-y-4">
    {#if previewData.length > 0}
      <div
        class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-3"
      >
        <p class="text-sm text-gray-600 dark:text-gray-400">
          Showing first {previewData.length} rows
        </p>
        <div class="flex space-x-2">
          <Button size="sm" variant="secondary" onclick={exportToCSV}>
            <FileDown size={14} class="mr-1" />
            Export CSV
          </Button>
          <Button size="sm" variant="secondary" onclick={exportToJSON}>
            <FileDown size={14} class="mr-1" />
            Export JSON
          </Button>
        </div>
      </div>

      <!-- Scrollable table container with fixed height -->
      <div class="border border-gray-200 dark:border-gray-700 rounded-lg">
        <div class="overflow-auto max-h-96 w-full">
          <table
            class="min-w-full divide-y divide-gray-200 dark:divide-gray-700"
          >
            <thead class="bg-gray-50 dark:bg-gray-800 sticky top-0">
              <tr>
                {#each previewColumns as column}
                  <th
                    class="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider whitespace-nowrap"
                    style="min-width: 150px;"
                  >
                    {column.label}
                  </th>
                {/each}
              </tr>
            </thead>
            <tbody
              class="bg-white dark:bg-gray-800 divide-y divide-gray-200 dark:divide-gray-700"
            >
              {#each previewData as row, i}
                <tr class="hover:bg-gray-50 dark:hover:bg-gray-700">
                  {#each previewColumns as column}
                    <td
                      class="px-4 py-3 text-sm text-gray-900 dark:text-white whitespace-nowrap overflow-hidden text-ellipsis"
                      style="max-width: 200px;"
                      title={row[column.key]}
                    >
                      {row[column.key] ?? "-"}
                    </td>
                  {/each}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      </div>
    {:else}
      <p class="text-center py-8 text-gray-500 dark:text-gray-400">
        No data available for preview
      </p>
    {/if}
  </div>
</Modal>

<!-- Toast Notifications -->
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
  }

  :global(.job-guid-highlight:hover) {
    background-color: #fed7aa;
    border-color: #f97316;
  }

  :global(.dark .job-guid-highlight) {
    background-color: #451a03;
    color: #fed7aa;
    border-color: #92400e;
  }

  :global(.dark .job-guid-highlight:hover) {
    background-color: #7c2d12;
    border-color: #ea580c;
  }
</style>