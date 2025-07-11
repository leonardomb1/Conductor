<script lang="ts">
  import { ChevronLeft, ChevronRight } from '@lucide/svelte';

  interface Column {
    key: string;
    label: string;
    sortable?: boolean;
    width?: string;
    render?: (value: any, row: any) => any;
  }

  interface Props {
    columns: Column[];
    data: any[];
    loading?: boolean;
    emptyMessage?: string;
    onSort?: (key: string, direction: 'asc' | 'desc') => void;
    sortKey?: string;
    sortDirection?: 'asc' | 'desc';
    pagination?: {
      currentPage: number;
      totalPages: number;
      pageSize: number;
      totalItems?: number;
      onPageChange: (page: number) => void;
    };
  }

  let {
    columns,
    data,
    loading = false,
    emptyMessage = 'No data available',
    onSort,
    sortKey,
    sortDirection = 'asc',
    pagination
  }: Props = $props();

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

  function getVisiblePages() {
    if (!pagination) return [];
    
    const { currentPage, totalPages } = pagination;
    const pages = [];
    const maxVisible = 5;
    
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
</script>

<div class="overflow-hidden shadow ring-1 ring-black ring-opacity-5 rounded-lg">
  <table class="min-w-full divide-y divide-supabase-gray-300">
    <thead class="bg-supabase-gray-50">
      <tr>
        {#each columns as column}
          <th
            class="px-6 py-3 text-left text-xs font-medium text-supabase-gray-500 uppercase tracking-wider"
            class:cursor-pointer={column.sortable}
            style={column.width ? `width: ${column.width}` : ''}
            onclick={() => handleSort(column)}
          >
            <div class="flex items-center space-x-1">
              <span>{column.label}</span>
              {#if column.sortable}
                <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  {#if sortKey === column.key}
                    {#if sortDirection === 'asc'}
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 15l7-7 7 7" />
                    {:else}
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7" />
                    {/if}
                  {:else}
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 9l4-4 4 4m0 6l-4 4-4-4" />
                  {/if}
                </svg>
              {/if}
            </div>
          </th>
        {/each}
      </tr>
    </thead>
    <tbody class="bg-white divide-y divide-supabase-gray-200">
      {#if loading}
        <tr>
          <td colspan={columns.length} class="px-6 py-4 text-center">
            <div class="flex justify-center">
              <svg class="animate-spin h-5 w-5 text-supabase-gray-500" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
            </div>
          </td>
        </tr>
      {:else if data.length === 0}
        <tr>
          <td colspan={columns.length} class="px-6 py-4 text-center text-supabase-gray-500">
            {emptyMessage}
          </td>
        </tr>
      {:else}
        {#each data as row, i}
          <tr class="hover:bg-supabase-gray-50">
            {#each columns as column}
              <td class="px-6 py-4 whitespace-nowrap text-sm text-supabase-gray-900">
                {#if column.render}
                  {@html column.render(row[column.key], row)}
                {:else}
                  {row[column.key] || '-'}
                {/if}
              </td>
            {/each}
          </tr>
        {/each}
      {/if}
    </tbody>
  </table>
  
  {#if pagination && pagination.totalPages > 1}
    <div class="bg-white px-4 py-3 flex items-center justify-between border-t border-supabase-gray-200 sm:px-6">
      <div class="flex-1 flex justify-between sm:hidden">
        <button
          onclick={() => goToPage(pagination.currentPage - 1)}
          disabled={pagination.currentPage <= 1}
          class="relative inline-flex items-center px-4 py-2 border border-supabase-gray-300 text-sm font-medium rounded-md text-supabase-gray-700 bg-white hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Previous
        </button>
        <button
          onclick={() => goToPage(pagination.currentPage + 1)}
          disabled={pagination.currentPage >= pagination.totalPages}
          class="ml-3 relative inline-flex items-center px-4 py-2 border border-supabase-gray-300 text-sm font-medium rounded-md text-supabase-gray-700 bg-white hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Next
        </button>
      </div>
      
      <div class="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
        <div>
          <p class="text-sm text-supabase-gray-700">
            Showing page <span class="font-medium">{pagination.currentPage}</span> of{' '}
            <span class="font-medium">{pagination.totalPages}</span>
            {#if pagination.totalItems}
              ({pagination.totalItems} total items)
            {/if}
          </p>
        </div>
        
        <div>
          <nav class="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
            <!-- Previous button -->
            <button
              onclick={() => goToPage(pagination.currentPage - 1)}
              disabled={pagination.currentPage <= 1}
              class="relative inline-flex items-center px-2 py-2 rounded-l-md border border-supabase-gray-300 bg-white text-sm font-medium text-supabase-gray-500 hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <span class="sr-only">Previous</span>
              <ChevronLeft class="h-5 w-5" />
            </button>
            
            <!-- Page numbers -->
            {#each getVisiblePages() as page}
              <button
                onclick={() => goToPage(page)}
                class="relative inline-flex items-center px-4 py-2 border text-sm font-medium"
                class:bg-supabase-green={page === pagination.currentPage}
                class:text-white={page === pagination.currentPage}
                class:border-supabase-green={page === pagination.currentPage}
                class:bg-white={page !== pagination.currentPage}
                class:text-supabase-gray-500={page !== pagination.currentPage}
                class:border-supabase-gray-300={page !== pagination.currentPage}
                class:hover:bg-supabase-gray-50={page !== pagination.currentPage}
              >
                {page}
              </button>
            {/each}
            
            <!-- Next button -->
            <button
              onclick={() => goToPage(pagination.currentPage + 1)}
              disabled={pagination.currentPage >= pagination.totalPages}
              class="relative inline-flex items-center px-2 py-2 rounded-r-md border border-supabase-gray-300 bg-white text-sm font-medium text-supabase-gray-500 hover:bg-supabase-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <span class="sr-only">Next</span>
              <ChevronRight class="h-5 w-5" />
            </button>
          </nav>
        </div>
      </div>
    </div>
  {/if}
</div>