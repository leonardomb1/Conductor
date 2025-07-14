<script lang="ts">
  import { page } from "$app/stores"
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import type { Extraction, Origin, Destination, Schedule } from "$lib/types.js"
  import PageHeader from "$lib/components/layout/PageHeader.svelte"
  import Card from "$lib/components/ui/Card.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Input from "$lib/components/ui/Input.svelte"
  import Select from "$lib/components/ui/Select.svelte"
  import Toast from "$lib/components/ui/Toast.svelte"
  import { ArrowLeft, Save } from "@lucide/svelte"

  let extraction = $state<Extraction | null>(null)
  let origins = $state<Origin[]>([])
  let destinations = $state<Destination[]>([])
  let schedules = $state<Schedule[]>([])
  let loading = $state(true)
  let saving = $state(false)

  // Toast notifications
  let toastMessage = $state("")
  let toastType = $state<"success" | "error" | "info">("info")
  let showToast = $state(false)

  // Form data - will be populated from extraction
  let formData = $state({
    extractionName: "",
    extractionAlias: "",
    originId: "",
    destinationId: "",
    scheduleId: "",
    sourceType: "db",
    indexName: "",
    isIncremental: false,
    isVirtual: false,
    virtualId: "",
    virtualIdGroup: "",
    isVirtualTemplate: false,
    filterColumn: "",
    filterCondition: "",
    filterTime: "",
    overrideQuery: "",
    dependencies: "",
    ignoreColumns: "",
    httpMethod: "GET",
    headerStructure: "",
    endpointFullName: "",
    bodyStructure: "",
    offsetAttr: "",
    offsetLimitAttr: "",
    pageAttr: "",
    paginationType: "",
    totalPageAttr: "",
    script: "",
  })

  let errors = $state<Record<string, string>>({})

  const extractionId = $derived(+$page.params.id)

  function showToastMessage(
    message: string,
    type: "success" | "error" | "info" = "info",
  ) {
    toastMessage = message
    toastType = type
    showToast = true
  }

  onMount(async () => {
    await Promise.all([loadExtraction(), loadRelatedData()])
  })

  async function loadExtraction() {
    try {
      const response = await api.getExtraction(extractionId)
      extraction = response.content?.[0] || null

      if (extraction) {
        // Populate form data
        formData = {
          extractionName: extraction.extractionName,
          extractionAlias: extraction.extractionAlias || "",
          originId: extraction.originId?.toString() || "",
          destinationId: extraction.destinationId?.toString() || "",
          scheduleId: extraction.scheduleId?.toString() || "",
          sourceType: extraction.sourceType || "db",
          indexName: extraction.indexName || "",
          isIncremental: extraction.isIncremental,
          isVirtual: extraction.isVirtual,
          virtualId: extraction.virtualId || "",
          virtualIdGroup: extraction.virtualIdGroup || "",
          isVirtualTemplate: extraction.isVirtualTemplate || false,
          filterColumn: extraction.filterColumn || "",
          filterCondition: extraction.filterCondition || "",
          filterTime: extraction.filterTime?.toString() || "",
          overrideQuery: extraction.overrideQuery || "",
          dependencies: extraction.dependencies || "",
          ignoreColumns: extraction.ignoreColumns || "",
          httpMethod: extraction.httpMethod || "GET",
          headerStructure: extraction.headerStructure || "",
          endpointFullName: extraction.endpointFullName || "",
          bodyStructure: extraction.bodyStructure || "",
          offsetAttr: extraction.offsetAttr || "",
          offsetLimitAttr: extraction.offsetLimitAttr || "",
          pageAttr: extraction.pageAttr || "",
          paginationType: extraction.paginationType || "",
          totalPageAttr: extraction.totalPageAttr || "",
          script: extraction.script || "",
        }
      }
    } catch (error) {
      showToastMessage("Failed to load extraction details", "error")
    }
  }

  async function loadRelatedData() {
    try {
      const [originsRes, destinationsRes, schedulesRes] = await Promise.all([
        api.getOrigins({ take: "1000" }),
        api.getDestinations({ take: "1000" }),
        api.getSchedules({ take: "1000" }),
      ])

      origins = originsRes.content || []
      destinations = destinationsRes.content || []
      schedules = schedulesRes.content || []
    } catch (error) {
      showToastMessage("Failed to load form options", "error")
    } finally {
      loading = false
    }
  }

  function validateForm(): boolean {
    errors = {}

    if (!formData.extractionName.trim()) {
      errors.extractionName = "Name is required"
    }

    if (!formData.originId) {
      errors.originId = "Origin is required"
    }

    if (!formData.indexName.trim()) {
      errors.indexName = "Index name is required"
    }

    if (formData.sourceType === "http" && !formData.endpointFullName.trim()) {
      errors.endpointFullName = "Endpoint URL is required for HTTP sources"
    }

    if (formData.filterTime && isNaN(+formData.filterTime)) {
      errors.filterTime = "Filter time must be a number"
    }

    return Object.keys(errors).length === 0
  }

  async function handleSubmit() {
    if (!validateForm()) return

    try {
      saving = true

      const extractionData = {
        extractionName: formData.extractionName,
        extractionAlias: formData.extractionAlias || undefined,
        originId: formData.originId ? +formData.originId : undefined,
        destinationId: formData.destinationId
          ? +formData.destinationId
          : undefined,
        scheduleId: formData.scheduleId ? +formData.scheduleId : undefined,
        sourceType: formData.sourceType,
        indexName: formData.indexName,
        isIncremental: formData.isIncremental,
        isVirtual: formData.isVirtual,
        virtualId: formData.virtualId || undefined,
        virtualIdGroup: formData.virtualIdGroup || undefined,
        isVirtualTemplate: formData.isVirtualTemplate || undefined,
        filterColumn: formData.filterColumn || undefined,
        filterCondition: formData.filterCondition || undefined,
        filterTime: formData.filterTime ? +formData.filterTime : undefined,
        overrideQuery: formData.overrideQuery || undefined,
        dependencies: formData.dependencies || undefined,
        ignoreColumns: formData.ignoreColumns || undefined,
        httpMethod: formData.httpMethod || undefined,
        headerStructure: formData.headerStructure || undefined,
        endpointFullName: formData.endpointFullName || undefined,
        bodyStructure: formData.bodyStructure || undefined,
        offsetAttr: formData.offsetAttr || undefined,
        offsetLimitAttr: formData.offsetLimitAttr || undefined,
        pageAttr: formData.pageAttr || undefined,
        paginationType: formData.paginationType || undefined,
        totalPageAttr: formData.totalPageAttr || undefined,
        script: formData.script || undefined,
      }

      const response = await api.updateExtraction(extractionId, extractionData)

     if (!response.error && (response.statusCode === 200 || response.statusCode === 204)) {
        showToastMessage("Extraction updated successfully", "success")
     } else {
        const errorMessage = response.information || "Unknown error occurred"
        showToastMessage(`Failed to update extraction: ${errorMessage}`, "error")
      }
    } catch (error) {
      showToastMessage(`Failed to update extraction: ${error.message}`, "error")
    } finally {
      saving = false
    }
  }

  const originOptions = $derived(
    origins.map((origin) => ({
      value: origin.id.toString(),
      label: origin.originName,
    })),
  )

  const destinationOptions = $derived(
    destinations.map((dest) => ({
      value: dest.id.toString(),
      label: dest.destinationName,
    })),
  )

  const scheduleOptions = $derived(
    schedules.map((schedule) => ({
      value: schedule.id.toString(),
      label: schedule.scheduleName,
    })),
  )
</script>

<svelte:head>
  <title>Edit {extraction?.extractionName || "Extraction"} - Conductor</title>
</svelte:head>

<div class="space-y-6">
  <PageHeader
    title="Edit Extraction"
    description={extraction?.extractionName
      ? `Editing "${extraction.extractionName}"`
      : "Loading..."}
  >
    {#snippet actions()}
      <div class="flex space-x-3">
        <Button
          variant="ghost"
          onclick={() =>
            (window.location.href = `/extractions/${extractionId}`)}
        >
          <ArrowLeft size={16} class="mr-2" />
          Cancel
        </Button>
        <Button variant="primary" loading={saving} onclick={handleSubmit}>
          <Save size={16} class="mr-2" />
          Save Changes
        </Button>
      </div>
    {/snippet}
  </PageHeader>

  {#if loading}
    <div class="flex justify-center py-12">
      <svg
        class="animate-spin h-8 w-8 text-supabase-gray-500"
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
  {:else}
    <form onsubmit={handleSubmit} class="space-y-6">
      <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <!-- Main Configuration -->
        <div class="lg:col-span-2 space-y-6">
          <Card title="Basic Information">
            <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
              <Input
                label="Name"
                bind:value={formData.extractionName}
                error={errors.extractionName}
                required
                placeholder="Enter extraction name"
              />
              <Input
                label="Alias"
                bind:value={formData.extractionAlias}
                placeholder="Optional alias"
              />
              <Input
                label="Index Name"
                bind:value={formData.indexName}
                error={errors.indexName}
                required
                placeholder="Primary key or index column"
              />
              <Select
                label="Source Type"
                bind:value={formData.sourceType}
                options={[
                  { value: "db", label: "Database" },
                  { value: "http", label: "HTTP API" },
                ]}
              />
            </div>
          </Card>

          <Card title="Relations">
            <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
              <Select
                label="Origin"
                bind:value={formData.originId}
                error={errors.originId}
                required
                placeholder="Select origin"
                options={originOptions}
              />
              <Select
                label="Destination"
                bind:value={formData.destinationId}
                placeholder="Select destination"
                options={destinationOptions}
              />
              <Select
                label="Schedule"
                bind:value={formData.scheduleId}
                placeholder="Select schedule"
                options={scheduleOptions}
              />
            </div>
          </Card>

          <Card title="Configuration Options">
            <div class="space-y-4">
              <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
                <label class="flex items-center space-x-2">
                  <input
                    type="checkbox"
                    bind:checked={formData.isIncremental}
                    class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
                  />
                  <span class="text-sm font-medium text-supabase-gray-700"
                    >Incremental</span
                  >
                </label>
                <label class="flex items-center space-x-2">
                  <input
                    type="checkbox"
                    bind:checked={formData.isVirtual}
                    class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
                  />
                  <span class="text-sm font-medium text-supabase-gray-700"
                    >Virtual</span
                  >
                </label>
                <label class="flex items-center space-x-2">
                  <input
                    type="checkbox"
                    bind:checked={formData.isVirtualTemplate}
                    class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
                  />
                  <span class="text-sm font-medium text-supabase-gray-700"
                    >Virtual Template</span
                  >
                </label>
              </div>

              <!-- Virtual fields - always visible -->
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <Input
                  label="Virtual ID"
                  bind:value={formData.virtualId}
                  placeholder="Virtual identifier"
                  help="Unique identifier for virtual extractions"
                />
                <Input
                  label="Virtual ID Group"
                  bind:value={formData.virtualIdGroup}
                  placeholder="Virtual group identifier"
                  help="Group identifier for related virtual extractions"
                />
              </div>
            </div>
          </Card>

          {#if formData.sourceType === "http"}
            <Card title="HTTP Configuration">
              <div class="space-y-4">
                <Input
                  label="Endpoint URL"
                  bind:value={formData.endpointFullName}
                  error={errors.endpointFullName}
                  placeholder="https://api.example.com/data"
                  required
                />
                <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <Select
                    label="HTTP Method"
                    bind:value={formData.httpMethod}
                    options={[
                      { value: "GET", label: "GET" },
                      { value: "POST", label: "POST" },
                    ]}
                  />
                  <Select
                    label="Pagination Type"
                    bind:value={formData.paginationType}
                    options={[
                      { value: "", label: "None" },
                      { value: "page", label: "Page Number" },
                      { value: "offset", label: "Offset/Limit" },
                    ]}
                  />
                </div>

                {#if formData.paginationType === "page"}
                  <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <Input
                      label="Page Attribute"
                      bind:value={formData.pageAttr}
                      placeholder="page"
                    />
                    <Input
                      label="Total Pages Attribute"
                      bind:value={formData.totalPageAttr}
                      placeholder="total_pages"
                    />
                  </div>
                {:else if formData.paginationType === "offset"}
                  <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <Input
                      label="Offset Attribute"
                      bind:value={formData.offsetAttr}
                      placeholder="offset"
                    />
                    <Input
                      label="Limit Attribute"
                      bind:value={formData.offsetLimitAttr}
                      placeholder="limit"
                    />
                  </div>
                {/if}

                <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label
                      for="headerStructure"
                      class="block text-sm font-medium text-supabase-gray-700 mb-1"
                    >
                      Headers
                    </label>
                    <textarea
                      id="headerStructure"
                      bind:value={formData.headerStructure}
                      class="form-textarea"
                      rows="3"
                      placeholder="Authorization=Bearer token,Content-Type=application/json"
                    ></textarea>
                    <p class="mt-1 text-xs text-supabase-gray-500">
                      Format: key=value,key2=value2
                    </p>
                  </div>
                  <div>
                    <label
                      for="bodyStructure"
                      class="block text-sm font-medium text-supabase-gray-700 mb-1"
                    >
                      Body Structure
                    </label>
                    <textarea
                      id="bodyStructure"
                      bind:value={formData.bodyStructure}
                      class="form-textarea"
                      rows="3"
                      placeholder="param1=value1,param2=value2"
                    ></textarea>
                    <p class="mt-1 text-xs text-supabase-gray-500">
                      Format: key=value,key2=value2
                    </p>
                  </div>
                </div>
              </div>
            </Card>
          {/if}

          <Card title="Filtering & Transformation">
            <div class="space-y-4">
              <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
                <Input
                  label="Filter Column"
                  bind:value={formData.filterColumn}
                  placeholder="updated_at"
                />
                <Input
                  label="Filter Time (seconds)"
                  type="number"
                  bind:value={formData.filterTime}
                  error={errors.filterTime}
                  placeholder="3600"
                />
                <Input
                  label="Dependencies"
                  bind:value={formData.dependencies}
                  placeholder="extraction1,extraction2"
                />
              </div>

              <Input
                label="Filter Condition"
                bind:value={formData.filterCondition}
                placeholder="AND status = 'active'"
              />

              <Input
                label="Ignore Columns"
                bind:value={formData.ignoreColumns}
                placeholder="password,secret_key"
              />

              <div>
                <label
                  for="overrideQuery"
                  class="block text-sm font-medium text-supabase-gray-700 mb-1"
                >
                  Override Query
                </label>
                <textarea
                  id="overrideQuery"
                  bind:value={formData.overrideQuery}
                  class="form-textarea"
                  rows="4"
                  placeholder="SELECT * FROM table WHERE condition"
                ></textarea>
              </div>

              <div>
                <label
                  for="customScript"
                  class="block text-sm font-medium text-supabase-gray-700 mb-1"
                >
                  Custom Script
                </label>
                <textarea
                  id="customScript"
                  bind:value={formData.script}
                  class="form-textarea"
                  rows="6"
                  placeholder=""
                ></textarea>
                <p class="mt-1 text-xs text-supabase-gray-500">
                  Custom C# script for advanced data processing
                </p>
              </div>
            </div>
          </Card>
        </div>

        <!-- Preview/Summary -->
        <div class="space-y-6">
          <Card title="Summary">
            <div class="space-y-3 text-sm">
              <div>
                <span class="font-medium text-supabase-gray-700">Name:</span>
                <p class="text-supabase-gray-900">
                  {formData.extractionName || "Not specified"}
                </p>
              </div>
              <div>
                <span class="font-medium text-supabase-gray-700"
                  >Source Type:</span
                >
                <p class="text-supabase-gray-900">{formData.sourceType}</p>
              </div>
              <div>
                <span class="font-medium text-supabase-gray-700">Origin:</span>
                <p class="text-supabase-gray-900">
                  {origins.find((o) => o.id.toString() === formData.originId)
                    ?.originName || "Not selected"}
                </p>
              </div>
              <div>
                <span class="font-medium text-supabase-gray-700"
                  >Destination:</span
                >
                <p class="text-supabase-gray-900">
                  {destinations.find(
                    (d) => d.id.toString() === formData.destinationId,
                  )?.destinationName || "Not selected"}
                </p>
              </div>
              <div>
                <span class="font-medium text-supabase-gray-700">Schedule:</span
                >
                <p class="text-supabase-gray-900">
                  {schedules.find(
                    (s) => s.id.toString() === formData.scheduleId,
                  )?.scheduleName || "Not selected"}
                </p>
              </div>
            </div>
          </Card>

          <Card title="Configuration Flags">
            <div class="space-y-2 text-sm">
              <div class="flex items-center justify-between">
                <span class="text-supabase-gray-700">Incremental</span>
                <span
                  class={formData.isIncremental
                    ? "text-green-600"
                    : "text-supabase-gray-400"}
                >
                  {formData.isIncremental ? "Yes" : "No"}
                </span>
              </div>
              <div class="flex items-center justify-between">
                <span class="text-supabase-gray-700">Virtual</span>
                <span
                  class={formData.isVirtual
                    ? "text-green-600"
                    : "text-supabase-gray-400"}
                >
                  {formData.isVirtual ? "Yes" : "No"}
                </span>
              </div>
              <div class="flex items-center justify-between">
                <span class="text-supabase-gray-700">Virtual Template</span>
                <span
                  class={formData.isVirtualTemplate
                    ? "text-green-600"
                    : "text-supabase-gray-400"}
                >
                  {formData.isVirtualTemplate ? "Yes" : "No"}
                </span>
              </div>
              <div class="flex items-center justify-between">
                <span class="text-supabase-gray-700">Has Filter</span>
                <span
                  class={formData.filterColumn
                    ? "text-green-600"
                    : "text-supabase-gray-400"}
                >
                  {formData.filterColumn ? "Yes" : "No"}
                </span>
              </div>
              <div class="flex items-center justify-between">
                <span class="text-supabase-gray-700">Has Script</span>
                <span
                  class={formData.script
                    ? "text-green-600"
                    : "text-supabase-gray-400"}
                >
                  {formData.script ? "Yes" : "No"}
                </span>
              </div>
              <div class="flex items-center justify-between">
                <span class="text-supabase-gray-700">Virtual ID</span>
                <span
                  class={formData.virtualId
                    ? "text-green-600"
                    : "text-supabase-gray-400"}
                >
                  {formData.virtualId ? "Set" : "Not set"}
                </span>
              </div>
              <div class="flex items-center justify-between">
                <span class="text-supabase-gray-700">Virtual Group</span>
                <span
                  class={formData.virtualIdGroup
                    ? "text-green-600"
                    : "text-supabase-gray-400"}
                >
                  {formData.virtualIdGroup ? "Set" : "Not set"}
                </span>
              </div>
            </div>
          </Card>
        </div>
      </div>
    </form>
  {/if}
</div>

<!-- Toast Notifications -->
<Toast bind:show={showToast} type={toastType} message={toastMessage} />