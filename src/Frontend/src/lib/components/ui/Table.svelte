<script lang="ts">
  import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from '@lucide/svelte';

  interface Column {
    key: string;
    label: string;
    sortable?: boolean;
    width?: string;
    render?: (value: any, row: any) => any;
  }

  interface PaginationInfo {
    currentPage: number;
    totalPages: number;
    pageSize: number;
    totalItems: number;
    onPageChange: (page: number) => void;
    onPageSizeChange?: (pageSize: number) => void;
  }

  interface Props {
    columns: Column[];
    data: any[];
    loading?: boolean;
    emptyMessage?: string;
    onSort?: (key: string, direction: 'asc' | 'desc') => void;
    sortKey?: string;
    sortDirection?: 'asc' | 'desc';
    pagination?: PaginationInfo;
    showPageSizeSelector?: boolean;
    mobileCardView?: boolean;
  }

  let {
    columns,
    data,
    loading = false,
    emptyMessage = 'No data available',
    onSort,
    sortKey,
    sortDirection = 'asc',
    pagination,
    showPageSizeSelector = true,
    mobileCardView = true
  }: Props = $props();

  const pageSizeOptions = [10, 20, 50, 100];

  function handleSort(column: Column) {
    if (!column.sortable || !onSort) return;
    
    const newDirection = sortKey === column.key && sortDirection === 'asc' ? 'desc' : 'asc';
    onSort(column.key, newDirection);
  }

  function goToPage(page: number) {
    if (pagination && page >= 1 && page <= pagination.totalPages) {
      pagination.onPageChange(page);
    }
  }

  function goToFirstPage() {
    if (pagination) {
      pagination.onPageChange(1);
    }
  }

  function goToLastPage() {
    if (pagination) {
      pagination.onPageChange(pagination.totalPages);
    }
  }

  function handlePageSizeChange(newPageSize: number) {
    if (pagination?.onPageSizeChange) {
      pagination.onPageSizeChange(newPageSize);
    }
  }

  function getVisiblePages() {
    if (!pagination) return [];
    
    const { currentPage, totalPages } = pagination;
    const pages = [];
    const maxVisible = 5; // Reduced for mobile
    
    let start = Math.max(1, currentPage - Math.floor(maxVisible / 2));
    let end = Math.min(totalPages, start + maxVisible - 1);
    
    if (end - start + 1 < maxVisible) {
      start = Math.max(1, end - maxVisible + 1);
    }
    
    for (let i = start; i <= end; i++) {
      pages.push(i);
    }
    
    return pages;
  }

  function formatItemCount() {
    if (!pagination) return '';
    
    const { currentPage, pageSize, totalItems } = pagination;
    const start = (currentPage - 1) * pageSize + 1;
    const end = Math.min(currentPage * pageSize, totalItems);
    
    return `${start}-${end} of ${totalItems.toLocaleString()}`;
  }
</script>

<div class="bg-white dark:bg-gray-800 shadow-sm dark:shadow-gray-900/20 ring-1 ring-gray-200 dark:ring-gray-700 rounded-lg overflow-hidden">
  <!-- Table Header with optional page size selector -->
  {#if pagination && showPageSizeSelector}
    <div class="px-4 sm:px-6 py-3 bg-gray-50 dark:bg-gray-900/50 border-b border-gray-200 dark:border-gray-700 flex flex-col sm:flex-row sm:justify-between sm:items-center gap-3">
      <div class="text-sm text-gray-700 dark:text-gray-300">
        Showing {formatItemCount()} items
      </div>
      
      <div class="flex items-center space-x-2">
        <label for="pageSize" class="text-sm text-gray-700 dark:text-gray-300">Per page:</label>
        <select
          id="pageSize"
          class="text-sm border-gray-300 dark:border-gray-600 dark:bg-gray-800 dark:text-white rounded-md focus:ring-supabase-green focus:border-supabase-green"
          value={pagination.pageSize}
          onchange={(e) => handlePageSizeChange(+e.target.value)}
        >
          {#each pageSizeOptions as size}
            <option value={size}>{size}</option>
          {/each}
        </select>
      </div>
    </div>
  {/if}

  <!-- Desktop Table View -->
  <div class="hidden sm:block overflow-x-auto">
    <table class="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
      <thead class="bg-gray-50 dark:bg-gray-900/50">
        <tr>
          {#each columns as column}
            <th
              class="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider"
              class:cursor-pointer={column.sortable}
              class:hover:bg-gray-100={column.sortable}
              class:dark:hover:bg-gray-800={column.sortable}
              style={column.width ? `width: ${column.width}` : ''}
              onclick={() => handleSort(column)}
            >
              <div class="flex items-center space-x-1">
                <span>{column.label}</span>
                {#if column.sortable}
                  <div class="flex flex-col">
                    <svg 
                      class="w-3 h-3 transition-colors"
                      class:text-supabase-green={sortKey === column.key && sortDirection === 'asc'}
                      class:text-gray-400={!(sortKey === column.key && sortDirection === 'asc')}
                      fill="currentColor" 
                      viewBox="0 0 20 20"
                    >
                      <path fill-rule="evenodd" d="M14.707 12.707a1 1 0 01-1.414 0L10 9.414l-3.293 3.293a1 1 0 01-1.414-1.414l4-4a1 1 0 011.414 0l4 4a1 1 0 010 1.414z" clip-rule="evenodd" />
                    </svg>
                    <svg 
                      class="w-3 h-3 -mt-1 transition-colors"
                      class:text-supabase-green={sortKey === column.key && sortDirection === 'desc'}
                      class:text-gray-400={!(sortKey === column.key && sortDirection === 'desc')}
                      fill="currentColor" 
                      viewBox="0 0 20 20"
                    >
                      <path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd" />
                    </svg>
                  </div>
                {/if}
              </div>
            </th>
          {/each}
        </tr>
      </thead>
      <tbody class="bg-white dark:bg-gray-800 divide-y divide-gray-200 dark:divide-gray-700">
        {#if loading}
          <tr>
            <td colspan={columns.length} class="px-6 py-12 text-center">
              <div class="flex flex-col items-center space-y-3">
                <svg class="animate-spin h-8 w-8 text-gray-500 dark:text-gray-400" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                <p class="text-sm text-gray-500 dark:text-gray-400">Loading data...</p>
              </div>
            </td>
          </tr>
        {:else if data.length === 0}
          <tr>
            <td colspan={columns.length} class="px-6 py-12 text-center">
              <div class="text-gray-500 dark:text-gray-400">
                <svg class="mx-auto h-12 w-12 text-gray-400 dark:text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                </svg>
                <p class="mt-2 text-sm font-medium">{emptyMessage}</p>
              </div>
            </td>
          </tr>
        {:else}
          {#each data as row, i}
            <tr class="hover:bg-gray-50 dark:hover:bg-gray-700/50 transition-colors">
              {#each columns as column}
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                  {#if column.render}
                    {@html column.render(row[column.key], row)}
                  {:else}
                    <span class="break-words">{row[column.key] ?? '-'}</span>
                  {/if}
                </td>
              {/each}
            </tr>
          {/each}
        {/if}
      </tbody>
    </table>
  </div>
<!-- Mobile Card View with Selection Support -->
  {#if mobileCardView}
    <div class="sm:hidden">
      {#if loading}
        <div class="p-6 text-center">
          <div class="flex flex-col items-center space-y-3">
            <svg class="animate-spin h-8 w-8 text-gray-500 dark:text-gray-400" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
            </svg>
            <p class="text-sm text-gray-500 dark:text-gray-400">Loading data...</p>
          </div>
        </div>
      {:else if data.length === 0}
        <div class="p-6 text-center">
          <div class="text-gray-500 dark:text-gray-400">
            <svg class="mx-auto h-12 w-12 text-gray-400 dark:text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
            <p class="mt-2 text-sm font-medium">{emptyMessage}</p>
          </div>
        </div>
      {:else}
        <div class="divide-y divide-gray-200 dark:divide-gray-700">
          {#each data as row, i}
            <div class="p-4 hover:bg-gray-50 dark:hover:bg-gray-700/50 transition-colors">
              <!-- Mobile Selection Header -->
              {#if columns.find(col => col.key === 'selection')}
                <div class="flex items-center justify-between mb-3 pb-2 border-b border-gray-100 dark:border-gray-600">
                  <div class="flex items-center space-x-3">
                    <!-- Selection Input with Better Mobile UX -->
                    <div class="flex items-center">
                      {@html columns.find(col => col.key === 'selection')?.render?.(null, row) || ''}
                    </div>
                    <!-- Primary identifier (usually name or title) -->
                    {#each columns.filter(col => col.key !== 'actions' && col.key !== 'selection') as column}
                      {#if column.key.toLowerCase().includes('name') || column.key.toLowerCase().includes('title')}
                        <span class="font-semibold text-gray-900 dark:text-white text-base">
                          {row[column.key] || '-'}
                        </span>
                      {/if}
                    {/each}
                  </div>
                  <!-- Quick Action Buttons -->
                  {#each columns.filter(col => col.key === 'actions') as actionsColumn}
                    <div class="flex space-x-1">
                      {@html actionsColumn.render?.(null, row) || ''}
                    </div>
                  {/each}
                </div>
              {/if}
              
              <!-- Mobile Card Content -->
              <div class="space-y-2">
                {#each columns.filter(col => col.key !== 'actions' && col.key !== 'selection') as column}
                  {#if row[column.key] !== undefined && row[column.key] !== null && row[column.key] !== '' && !column.key.toLowerCase().includes('name')}
                    <div class="flex justify-between items-start">
                      <span class="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                        {column.label}:
                      </span>
                      <span class="text-sm text-gray-900 dark:text-gray-100 text-right ml-2">
                        {#if column.render}
                          {@html column.render(row[column.key], row)}
                        {:else}
                          {row[column.key] ?? '-'}
                        {/if}
                      </span>
                    </div>
                  {/if}
                {/each}
                
                <!-- Actions at bottom if not already shown and no selection -->
                {#if !columns.find(col => col.key === 'selection')}
                  {#each columns.filter(col => col.key === 'actions') as actionsColumn}
                    <div class="pt-2 border-t border-gray-200 dark:border-gray-700">
                      {@html actionsColumn.render?.(null, row) || ''}
                    </div>
                  {/each}
                {/if}
              </div>
            </div>
          {/each}
        </div>
      {/if}
    </div>
  {/if}
  <!-- Enhanced Pagination -->
  {#if pagination && pagination.totalPages > 1}
    <div class="bg-white dark:bg-gray-800 px-4 py-3 flex flex-col sm:flex-row items-center justify-between border-t border-gray-200 dark:border-gray-700 gap-3">
      <!-- Mobile pagination -->
      <div class="flex justify-between w-full sm:hidden">
        <button
          onclick={() => goToPage(pagination.currentPage - 1)}
          disabled={pagination.currentPage <= 1}
          class="relative inline-flex items-center px-4 py-2 border border-gray-300 dark:border-gray-600 text-sm font-medium rounded-md text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-800 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          Previous
        </button>
        <span class="text-sm text-gray-700 dark:text-gray-300 self-center">
          {pagination.currentPage} / {pagination.totalPages}
        </span>
        <button
          onclick={() => goToPage(pagination.currentPage + 1)}
          disabled={pagination.currentPage >= pagination.totalPages}
          class="relative inline-flex items-center px-4 py-2 border border-gray-300 dark:border-gray-600 text-sm font-medium rounded-md text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-800 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          Next
        </button>
      </div>
      
      <!-- Desktop pagination -->
      <div class="hidden sm:flex sm:flex-1 sm:items-center sm:justify-between">
        <div>
          <p class="text-sm text-gray-700 dark:text-gray-300">
            Showing {formatItemCount()} items
          </p>
        </div>
        
        <div class="flex items-center space-x-2">
          <!-- First page -->
          <button
            onclick={goToFirstPage}
            disabled={pagination.currentPage <= 1}
            class="relative inline-flex items-center px-2 py-2 border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-sm font-medium text-gray-500 dark:text-gray-400 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed rounded-md transition-colors"
            title="First page"
          >
            <ChevronsLeft class="h-4 w-4" />
          </button>
          
          <!-- Previous page -->
          <button
            onclick={() => goToPage(pagination.currentPage - 1)}
            disabled={pagination.currentPage <= 1}
            class="relative inline-flex items-center px-2 py-2 border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-sm font-medium text-gray-500 dark:text-gray-400 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed rounded-md transition-colors"
            title="Previous page"
          >
            <ChevronLeft class="h-4 w-4" />
          </button>
          
          <!-- Page numbers -->
          <div class="flex items-center space-x-1">
            {#each getVisiblePages() as page}
              <button
                onclick={() => goToPage(page)}
                class="relative inline-flex items-center px-3 py-2 text-sm font-medium border rounded-md transition-colors"
                class:bg-supabase-green={page === pagination.currentPage}
                class:text-white={page === pagination.currentPage}
                class:border-supabase-green={page === pagination.currentPage}
                class:bg-white={page !== pagination.currentPage}
                class:dark:bg-gray-800={page !== pagination.currentPage}
                class:text-gray-700={page !== pagination.currentPage}
                class:dark:text-gray-300={page !== pagination.currentPage}
                class:border-gray-300={page !== pagination.currentPage}
                class:dark:border-gray-600={page !== pagination.currentPage}
                class:hover:bg-gray-50={page !== pagination.currentPage}
                class:dark:hover:bg-gray-700={page !== pagination.currentPage}
              >
                {page}
              </button>
            {/each}
          </div>
          
          <!-- Next page -->
          <button
            onclick={() => goToPage(pagination.currentPage + 1)}
            disabled={pagination.currentPage >= pagination.totalPages}
            class="relative inline-flex items-center px-2 py-2 border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-sm font-medium text-gray-500 dark:text-gray-400 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed rounded-md transition-colors"
            title="Next page"
          >
            <ChevronRight class="h-4 w-4" />
          </button>
          
          <!-- Last page -->
          <button
            onclick={goToLastPage}
            disabled={pagination.currentPage >= pagination.totalPages}
            class="relative inline-flex items-center px-2 py-2 border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 text-sm font-medium text-gray-500 dark:text-gray-400 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed rounded-md transition-colors"
            title="Last page"
          >
            <ChevronsRight class="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  {/if}
</div>