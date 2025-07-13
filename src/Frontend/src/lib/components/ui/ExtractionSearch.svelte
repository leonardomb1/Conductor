<script lang="ts">
  import { onMount } from "svelte"
  import { api } from "$lib/api.js"
  import type {
    Extraction,
    SearchSuggestion,
    ExtractionAggregations,
  } from "$lib/types.js"
  import Input from "$lib/components/ui/Input.svelte"
  import Select from "$lib/components/ui/Select.svelte"
  import Button from "$lib/components/ui/Button.svelte"
  import Badge from "$lib/components/ui/Badge.svelte"
  import Card from "$lib/components/ui/Card.svelte"
  import {
    Search,
    Filter,
    X,
    ChevronDown,
    ChevronUp,
    TrendingUp,
    Clock,
    BarChart3,
    Settings,
  } from "@lucide/svelte"

  // Props
  let onSearchResults: (
    results: Extraction[],
    totalCount: number,
  ) => void = () => {}
  let onSelectionChange: (selectedIds: number[]) => void = () => {}

  // Search state
  let searchTerm = $state("")
  let searchLoading = $state(false)
  let suggestions = $state<SearchSuggestion[]>([])
  let showSuggestions = $state(false)
  let searchResults = $state<Extraction[]>([])
  let totalCount = $state(0)
  let aggregations = $state<ExtractionAggregations | null>(null)

  // Advanced filters state
  let showAdvancedFilters = $state(false)
  let filters = $state({
    // Basic filters
    origin: "",
    destination: "",
    schedule: "",
    sourceType: "",

    // Boolean filters
    isIncremental: "",
    isVirtual: "",
    hasDestination: "",
    hasSchedule: "",
    hasDependencies: "",
    hasScript: "",

    // Advanced filters
    alias: "",
    indexName: "",
    filterColumn: "",
    httpMethod: "",
    paginationType: "",

    // Range filters
    minFilterTime: "",
    maxFilterTime: "",

    // Sorting
    sortBy: "name",
    sortOrder: "asc",
  })

  // Quick filter presets
  let quickFilters = $state({
    withoutDestination: false,
    withoutSchedule: false,
    incrementalOnly: false,
    httpOnly: false,
    recentlyCreated: false,
  })

  // Recent searches and popular extractions
  let recentExtractions = $state<Extraction[]>([])
  let popularExtractions = $state<Extraction[]>([])
  let showQuickAccess = $state(true)

  // Debounce timer
  let searchDebounceTimer: NodeJS.Timeout | null = null
  let suggestionsDebounceTimer: NodeJS.Timeout | null = null

  onMount(async () => {
    await loadQuickAccessData()
    await loadAggregations()
  })

  async function loadQuickAccessData() {
    try {
      const [recent, popular] = await Promise.all([
        api.getRecentExtractions(5),
        api.getPopularExtractions(5),
      ])

      recentExtractions = recent.content || []
      popularExtractions = popular.content || []
    } catch (error) {
      console.error("Failed to load quick access data:", error)
    }
  }

  async function loadAggregations() {
    try {
      const response = await api.getExtractionAggregations()
      aggregations = response.content?.[0] || null
    } catch (error) {
      console.error("Failed to load aggregations:", error)
    }
  }

  // Debounced search function
  function debounceSearch() {
    if (searchDebounceTimer) {
      clearTimeout(searchDebounceTimer)
    }

    searchDebounceTimer = setTimeout(async () => {
      await performSearch()
    }, 300)
  }

  // Debounced suggestions function
  function debounceSuggestions() {
    if (suggestionsDebounceTimer) {
      clearTimeout(suggestionsDebounceTimer)
    }

    suggestionsDebounceTimer = setTimeout(async () => {
      await getSuggestions()
    }, 150)
  }

  async function performSearch() {
    if (!searchTerm.trim() && !hasActiveFilters()) {
      searchResults = []
      totalCount = 0
      onSearchResults([], 0)
      return
    }

    try {
      searchLoading = true

      const searchParams = buildSearchParams()
      const response = await api.searchExtractions(searchParams)

      searchResults = response.content || []
      totalCount = response.entityCount || 0

      onSearchResults(searchResults, totalCount)

      // Reload aggregations with current filters
      if (hasActiveFilters()) {
        const aggResponse = await api.getExtractionAggregations(searchParams)
        aggregations = aggResponse.content?.[0] || null
      }
    } catch (error) {
      console.error("Search failed:", error)
      searchResults = []
      totalCount = 0
      onSearchResults([], 0)
    } finally {
      searchLoading = false
    }
  }

  async function getSuggestions() {
    if (searchTerm.length < 2) {
      suggestions = []
      showSuggestions = false
      return
    }

    try {
      const response = await api.getSearchSuggestions(searchTerm)
      suggestions = response.content || []
      showSuggestions = suggestions.length > 0
    } catch (error) {
      console.error("Failed to get suggestions:", error)
      suggestions = []
      showSuggestions = false
    }
  }

  function buildSearchParams() {
    const params: any = {}

    if (searchTerm.trim()) params.q = searchTerm

    // Apply filters
    Object.entries(filters).forEach(([key, value]) => {
      if (value && value !== "") {
        params[key] = value
      }
    })

    // Apply quick filters
    if (quickFilters.withoutDestination) params.hasDestination = "false"
    if (quickFilters.withoutSchedule) params.hasSchedule = "false"
    if (quickFilters.incrementalOnly) params.isIncremental = "true"
    if (quickFilters.httpOnly) params.sourceType = "http"

    return params
  }

  function hasActiveFilters(): boolean {
    return (
      Object.values(filters).some((v) => v && v !== "") ||
      Object.values(quickFilters).some((v) => v)
    )
  }

  function clearAllFilters() {
    searchTerm = ""
    filters = {
      origin: "",
      destination: "",
      schedule: "",
      sourceType: "",
      isIncremental: "",
      isVirtual: "",
      hasDestination: "",
      hasSchedule: "",
      hasDependencies: "",
      hasScript: "",
      alias: "",
      indexName: "",
      filterColumn: "",
      httpMethod: "",
      paginationType: "",
      minFilterTime: "",
      maxFilterTime: "",
      sortBy: "name",
      sortOrder: "asc",
    }
    quickFilters = {
      withoutDestination: false,
      withoutSchedule: false,
      incrementalOnly: false,
      httpOnly: false,
      recentlyCreated: false,
    }

    suggestions = []
    showSuggestions = false
    performSearch()
  }

  function applySuggestion(suggestion: SearchSuggestion) {
    if (suggestion.type === "extraction") {
      searchTerm = suggestion.value
    } else if (suggestion.type === "origin") {
      filters.origin = suggestion.value
    } else if (suggestion.type === "destination") {
      filters.destination = suggestion.value
    } else if (suggestion.type === "schedule") {
      filters.schedule = suggestion.value
    }

    showSuggestions = false
    performSearch()
  }

  function selectExtraction(extraction: Extraction) {
    searchTerm = extraction.extractionName
    performSearch()
  }

  // Watch for search term changes
  $effect(() => {
    if (searchTerm !== undefined) {
      debounceSearch()
      debounceSuggestions()
    }
  })

  // Watch for filter changes
  $effect(() => {
    if (filters && quickFilters) {
      performSearch()
    }
  })
</script>

<div class="space-y-6">
  <!-- Main Search Bar -->
  <div class="relative">
    <div class="relative">
      <Search
        class="absolute left-3 top-1/2 transform -translate-y-1/2 h-5 w-5 text-supabase-gray-400"
      />
      <input
        type="text"
        bind:value={searchTerm}
        placeholder="Search extractions by name, origin, destination..."
        class="w-full pl-10 pr-12 py-3 border border-supabase-gray-300 rounded-lg text-lg focus:ring-2 focus:ring-supabase-green focus:border-supabase-green"
        class:border-supabase-green={showSuggestions}
      />

      {#if searchLoading}
        <div class="absolute right-3 top-1/2 transform -translate-y-1/2">
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
            suggestions = []
            showSuggestions = false
          }}
          class="absolute right-3 top-1/2 transform -translate-y-1/2 text-supabase-gray-400 hover:text-supabase-gray-600"
        >
          <X class="h-5 w-5" />
        </button>
      {/if}
    </div>

    <!-- Search Suggestions Dropdown -->
    {#if showSuggestions && suggestions.length > 0}
      <div
        class="absolute z-50 w-full mt-1 bg-white rounded-lg shadow-lg border border-supabase-gray-200 max-h-80 overflow-y-auto"
      >
        {#each suggestions as suggestion}
          <button
            onclick={() => applySuggestion(suggestion)}
            class="w-full px-4 py-3 text-left hover:bg-supabase-gray-50 flex items-center justify-between border-b border-supabase-gray-100 last:border-b-0"
          >
            <div class="flex items-center space-x-3">
              <Badge
                variant={suggestion.type === "extraction" ? "default" : "info"}
                size="sm"
              >
                {suggestion.type}
              </Badge>
              <span class="text-supabase-gray-900">{suggestion.label}</span>
            </div>
            <span class="text-xs text-supabase-gray-500"
              >{suggestion.count}</span
            >
          </button>
        {/each}
      </div>
    {/if}
  </div>

  <!-- Quick Filters and Advanced Controls -->
  <div class="flex flex-wrap items-center gap-3">
    <!-- Quick Filter Toggles -->
    <div class="flex items-center space-x-2">
      <span class="text-sm font-medium text-supabase-gray-700"
        >Quick filters:</span
      >

      <label class="flex items-center space-x-1 cursor-pointer">
        <input
          type="checkbox"
          bind:checked={quickFilters.withoutDestination}
          class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
        />
        <span class="text-sm text-supabase-gray-600">No destination</span>
      </label>

      <label class="flex items-center space-x-1 cursor-pointer">
        <input
          type="checkbox"
          bind:checked={quickFilters.incrementalOnly}
          class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
        />
        <span class="text-sm text-supabase-gray-600">Incremental only</span>
      </label>

      <label class="flex items-center space-x-1 cursor-pointer">
        <input
          type="checkbox"
          bind:checked={quickFilters.httpOnly}
          class="rounded border-supabase-gray-300 text-supabase-green focus:ring-supabase-green"
        />
        <span class="text-sm text-supabase-gray-600">HTTP sources</span>
      </label>
    </div>

    <div class="flex items-center space-x-2 ml-auto">
      <!-- Advanced Filters Toggle -->
      <Button
        variant="ghost"
        size="sm"
        onclick={() => (showAdvancedFilters = !showAdvancedFilters)}
      >
        <Filter size={16} class="mr-2" />
        Advanced
        {#if showAdvancedFilters}
          <ChevronUp size={16} class="ml-1" />
        {:else}
          <ChevronDown size={16} class="ml-1" />
        {/if}
      </Button>

      <!-- Clear Filters -->
      {#if hasActiveFilters()}
        <Button variant="ghost" size="sm" onclick={clearAllFilters}>
          <X size={16} class="mr-2" />
          Clear all
        </Button>
      {/if}

      <!-- Results Summary -->
      {#if totalCount > 0}
        <span
          class="text-sm text-supabase-gray-600 bg-supabase-gray-100 px-3 py-1 rounded-full"
        >
          {totalCount.toLocaleString()} results
        </span>
      {/if}
    </div>
  </div>

  <!-- Advanced Filters Panel -->
  {#if showAdvancedFilters}
    <Card title="Advanced Filters">
      <div class="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 gap-4">
        <!-- Entity Filters -->
        <div class="space-y-3">
          <h4 class="text-sm font-medium text-supabase-gray-900">Entities</h4>
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

        <!-- Type and Configuration -->
        <div class="space-y-3">
          <h4 class="text-sm font-medium text-supabase-gray-900">
            Configuration
          </h4>
          <Select
            placeholder="Source type"
            bind:value={filters.sourceType}
            options={[
              { value: "", label: "All types" },
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
              { value: "false", label: "Full load" },
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

        <!-- Advanced Properties -->
        <div class="space-y-3">
          <h4 class="text-sm font-medium text-supabase-gray-900">Properties</h4>
          <Input
            placeholder="Alias contains"
            bind:value={filters.alias}
            size="sm"
          />
          <Input
            placeholder="Index name"
            bind:value={filters.indexName}
            size="sm"
          />
          <Input
            placeholder="Filter column"
            bind:value={filters.filterColumn}
            size="sm"
          />
        </div>

        <!-- Sorting and Ranges -->
        <div class="space-y-3">
          <h4 class="text-sm font-medium text-supabase-gray-900">
            Sorting & Ranges
          </h4>
          <Select
            placeholder="Sort by"
            bind:value={filters.sortBy}
            options={[
              { value: "name", label: "Name" },
              { value: "sourceType", label: "Source Type" },
              { value: "origin", label: "Origin" },
              { value: "destination", label: "Destination" },
              { value: "schedule", label: "Schedule" },
            ]}
          />
          <Select
            bind:value={filters.sortOrder}
            options={[
              { value: "asc", label: "Ascending" },
              { value: "desc", label: "Descending" },
            ]}
          />
          <div class="grid grid-cols-2 gap-2">
            <Input
              placeholder="Min filter time"
              type="number"
              bind:value={filters.minFilterTime}
              size="sm"
            />
            <Input
              placeholder="Max filter time"
              type="number"
              bind:value={filters.maxFilterTime}
              size="sm"
            />
          </div>
        </div>
      </div>
    </Card>
  {/if}

  <!-- Quick Access Panel -->
  {#if showQuickAccess && !searchTerm && !hasActiveFilters()}
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <!-- Recent Extractions -->
      <Card title="Recent Extractions">
        {#snippet children()}
          <div class="space-y-2">
            {#each recentExtractions as extraction}
              <button
                onclick={() => selectExtraction(extraction)}
                class="w-full p-3 text-left hover:bg-supabase-gray-50 rounded-md border border-supabase-gray-200 transition-colors"
              >
                <div class="flex items-center justify-between">
                  <div>
                    <p class="text-sm font-medium text-supabase-gray-900">
                      {extraction.extractionName}
                    </p>
                    <p class="text-xs text-supabase-gray-500">
                      {extraction.origin?.originName || "No origin"} → {extraction
                        .destination?.destinationName || "No destination"}
                    </p>
                  </div>
                  <Badge
                    variant={extraction.sourceType === "http"
                      ? "info"
                      : "success"}
                    size="sm"
                  >
                    {extraction.sourceType || "db"}
                  </Badge>
                </div>
              </button>
            {/each}

            {#if recentExtractions.length === 0}
              <p class="text-sm text-supabase-gray-500 text-center py-4">
                No recent extractions
              </p>
            {/if}
          </div>
        {/snippet}
      </Card>

      <!-- Popular Extractions -->
      <Card title="Popular Extractions">
        {#snippet children()}
          <div class="space-y-2">
            {#each popularExtractions as extraction}
              <button
                onclick={() => selectExtraction(extraction)}
                class="w-full p-3 text-left hover:bg-supabase-gray-50 rounded-md border border-supabase-gray-200 transition-colors"
              >
                <div class="flex items-center justify-between">
                  <div>
                    <p class="text-sm font-medium text-supabase-gray-900">
                      {extraction.extractionName}
                    </p>
                    <p class="text-xs text-supabase-gray-500">
                      {extraction.origin?.originName || "No origin"}
                      {#if extraction.schedule}
                        • Scheduled
                      {/if}
                    </p>
                  </div>
                  <TrendingUp size={16} class="text-supabase-green" />
                </div>
              </button>
            {/each}

            {#if popularExtractions.length === 0}
              <p class="text-sm text-supabase-gray-500 text-center py-4">
                No popular extractions
              </p>
            {/if}
          </div>
        {/snippet}
      </Card>

      <!-- Statistics Overview -->
      {#if aggregations}
        <Card title="Overview">
          {#snippet children()}
            <div class="space-y-4">
              <div class="grid grid-cols-2 gap-4">
                <div class="text-center">
                  <div class="text-2xl font-bold text-supabase-gray-900">
                    {aggregations.totalCount}
                  </div>
                  <div class="text-xs text-supabase-gray-500">
                    Total Extractions
                  </div>
                </div>
                <div class="text-center">
                  <div class="text-2xl font-bold text-orange-600">
                    {aggregations.withoutDestination}
                  </div>
                  <div class="text-xs text-supabase-gray-500">
                    No Destination
                  </div>
                </div>
              </div>

              <div class="space-y-2">
                <h5 class="text-xs font-medium text-supabase-gray-700">
                  By Source Type
                </h5>
                {#each aggregations.bySourceType as item}
                  <div class="flex items-center justify-between text-sm">
                    <span class="text-supabase-gray-600 capitalize"
                      >{item.category}</span
                    >
                    <Badge variant="default" size="sm">{item.count}</Badge>
                  </div>
                {/each}
              </div>

              {#if aggregations.byOrigin.length > 0}
                <div class="space-y-2">
                  <h5 class="text-xs font-medium text-supabase-gray-700">
                    Top Origins
                  </h5>
                  {#each aggregations.byOrigin.slice(0, 3) as item}
                    <div class="flex items-center justify-between text-sm">
                      <span class="text-supabase-gray-600 truncate"
                        >{item.category}</span
                      >
                      <Badge variant="info" size="sm">{item.count}</Badge>
                    </div>
                  {/each}
                </div>
              {/if}
            </div>
          {/snippet}
        </Card>
      {/if}
    </div>
  {/if}

  <!-- Search Results Summary -->
  {#if searchResults.length > 0 || searchTerm || hasActiveFilters()}
    <div class="flex items-center justify-between">
      <div class="flex items-center space-x-4">
        <h3 class="text-lg font-medium text-supabase-gray-900">
          {#if searchTerm}
            Search results for "{searchTerm}"
          {:else}
            Filtered results
          {/if}
        </h3>

        {#if totalCount > 0}
          <Badge variant="default">{totalCount.toLocaleString()} found</Badge>
        {/if}
      </div>

      {#if hasActiveFilters()}
        <div class="flex items-center space-x-2">
          <span class="text-sm text-supabase-gray-500">Active filters:</span>
          {#if quickFilters.withoutDestination}
            <Badge variant="warning" size="sm">No destination</Badge>
          {/if}
          {#if quickFilters.incrementalOnly}
            <Badge variant="info" size="sm">Incremental</Badge>
          {/if}
          {#if quickFilters.httpOnly}
            <Badge variant="info" size="sm">HTTP</Badge>
          {/if}
          {#if filters.origin}
            <Badge variant="default" size="sm">Origin: {filters.origin}</Badge>
          {/if}
          {#if filters.destination}
            <Badge variant="default" size="sm"
              >Dest: {filters.destination}</Badge
            >
          {/if}
        </div>
      {/if}
    </div>
  {/if}
</div>

<style>
  /* Custom scrollbar for suggestions dropdown */
  .overflow-y-auto::-webkit-scrollbar {
    width: 6px;
  }

  .overflow-y-auto::-webkit-scrollbar-track {
    background: #f1f5f9;
  }

  .overflow-y-auto::-webkit-scrollbar-thumb {
    background: #cbd5e1;
    border-radius: 3px;
  }

  .overflow-y-auto::-webkit-scrollbar-thumb:hover {
    background: #94a3b8;
  }
</style>
