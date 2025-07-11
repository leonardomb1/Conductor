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
    showPageSizeSelector = true
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
    const maxVisible = 7; // Show more pages for better navigation
    
    let start = Math.max(1, currentPage - Math.floor(maxVisible / 2));
    let end = Math.min(totalPages, start + maxVisible - 1);
    
    // Adjust start if we're near the end
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
    
    return `Showing ${start}-${end} of ${totalItems.toLocaleString()} items`;
  }
</script>

<div class="bg-white shadow ring-1 ring-black ring-opacity-5 rounded-lg overflow-hidden">
  <!-- Table Header with optional page size selector -->
  {#if pagination && showPageSizeSelector}
    <div class="px-6 py-3 bg-gray-50 border-b border-gray-200 flex justify-between items-center">
      <div class="text-sm text-gray-700">
        {formatItemCount()}
      </div>
      
      <div class="flex items-center space-x-2">
        <label for="pageSize" class="text-sm text-gray-700">Items per page:</label>
        <select
          id="pageSize"
          class="text-sm border-gray-300 rounded-md focus:ring-supabase-green focus:border-supabase-green"
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

  <!-- Table -->
  <div class="overflow-x-auto">
    <table class="min-w-full divide-y divide-supabase-gray-300">
      <thead class="bg-supabase-gray-50">
        <tr>
          {#each columns as column}
            <th
              class="px-6 py-3 text-left text-xs font-medium text-supabase-gray-500 uppercase tracking-wider"
              class:cursor-pointer={column.sortable}
              class:hover:bg-supabase-gray-100={column.sortable}
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
      <tbody class="bg-white divide-y divide-supabase-gray-200">
        {#if loading}
          <tr>
            <td colspan={columns.length} class="px-6 py-12 text-center">
              <div class="flex flex-col items-center space-y-3">
                <svg class="animate-spin h-8 w-8 text-supabase-gray-500" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                <p class="text-sm text-supabase-gray-500">Loading data...</p>
              </div>
            </td>
          </tr>
        {:else if data.length === 0}
          <tr>
            <td colspan={columns.length} class="px-6 py-12 text-center">
              <div class="text-supabase-gray-500">
                <svg class="mx-auto h-12 w-12 text-supabase-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                </svg>
                <p class="mt-2 text-sm font-medium">{emptyMessage}</p>
              </div>
            </td>
          </tr>
        {:else}
          {#each data as row, i}
            <tr class="hover:bg-supabase-gray-50 transition-colors">
              {#each columns as column}
                <td class="px-6 py-4 whitespace-nowrap text-sm text-supabase-gray-900">
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
  
  <!-- Enhanced Pagination -->
  {#if pagination && pagination.totalPages > 1}
    <div class="bg-white px-4 py-3 flex items-center justify-between border-t border-supabase-gray-200 sm:px-6">
      <!-- Mobile pagination -->
      <div class="flex-1 flex justify-between sm:hidden">
        <button
          onclick={() => goToPage(pagination.currentPage - 1)}
          disabled={pagination.currentPage <= 1}
          class="relative inline-flex items-center px-4 py-2 border border-supabase-gray-300 text-sm font-medium rounded-md text-supabase-gray-700 bg-white hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          Previous
        </button>
        <span class="text-sm text-supabase-gray-700 self-center">
          Page {pagination.currentPage} of {pagination.totalPages}
        </span>
        <button
          onclick={() => goToPage(pagination.currentPage + 1)}
          disabled={pagination.currentPage >= pagination.totalPages}
          class="ml-3 relative inline-flex items-center px-4 py-2 border border-supabase-gray-300 text-sm font-medium rounded-md text-supabase-gray-700 bg-white hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          Next
        </button>
      </div>
      
      <!-- Desktop pagination -->
      <div class="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
        <div>
          <p class="text-sm text-supabase-gray-700">
            {formatItemCount()}
          </p>
        </div>
        
        <div class="flex items-center space-x-2">
          <!-- First page -->
          <button
            onclick={goToFirstPage}
            disabled={pagination.currentPage <= 1}
            class="relative inline-flex items-center px-2 py-2 border border-supabase-gray-300 bg-white text-sm font-medium text-supabase-gray-500 hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed rounded-md transition-colors"
            title="First page"
          >
            <ChevronsLeft class="h-4 w-4" />
          </button>
          
          <!-- Previous page -->
          <button
            onclick={() => goToPage(pagination.currentPage - 1)}
            disabled={pagination.currentPage <= 1}
            class="relative inline-flex items-center px-2 py-2 border border-supabase-gray-300 bg-white text-sm font-medium text-supabase-gray-500 hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed rounded-md transition-colors"
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
                class:text-supabase-gray-700={page !== pagination.currentPage}
                class:border-supabase-gray-300={page !== pagination.currentPage}
                class:hover:bg-supabase-gray-50={page !== pagination.currentPage}
              >
                {page}
              </button>
            {/each}
          </div>
          
          <!-- Next page -->
          <button
            onclick={() => goToPage(pagination.currentPage + 1)}
            disabled={pagination.currentPage >= pagination.totalPages}
            class="relative inline-flex items-center px-2 py-2 border border-supabase-gray-300 bg-white text-sm font-medium text-supabase-gray-500 hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed rounded-md transition-colors"
            title="Next page"
          >
            <ChevronRight class="h-4 w-4" />
          </button>
          
          <!-- Last page -->
          <button
            onclick={goToLastPage}
            disabled={pagination.currentPage >= pagination.totalPages}
            class="relative inline-flex items-center px-2 py-2 border border-supabase-gray-300 bg-white text-sm font-medium text-supabase-gray-500 hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed rounded-md transition-colors"
            title="Last page"
          >
            <ChevronsRight class="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  {/if}
</div>