<script lang="ts">
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
  }

  let {
    columns,
    data,
    loading = false,
    emptyMessage = 'No data available',
    onSort,
    sortKey,
    sortDirection = 'asc'
  }: Props = $props();

  function handleSort(column: Column) {
    if (!column.sortable || !onSort) return;
    
    const newDirection = sortKey === column.key && sortDirection === 'asc' ? 'desc' : 'asc';
    onSort(column.key, newDirection);
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
</div>
