<script lang="ts">
  import { Button } from '$lib/components/ui/Button.svelte';
  import { Badge } from '$lib/components/ui/Badge.svelte';
  import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '$lib/components/ui/Table.svelte';

  interface Props {
    data?: any[];
    onEdit?: ((item: any) => void) | null;
    onDelete?: ((item: any) => void) | null;
    currentPage?: number;
    totalPages?: number;
    onPageChange?: ((page: number) => void) | null;
    pageSize?: number;
    showPagination?: boolean;
    hideColumns?: string[];
    editMode?: boolean;
  }

  let {
    data = [],
    onEdit = null,
    onDelete = null,
    currentPage = 1,
    totalPages = 1,
    onPageChange = null,
    pageSize = 10,
    showPagination = true,
    hideColumns = [],
    editMode = false
  }: Props = $props();

  let paginatedData = $derived(showPagination ? data.slice((currentPage - 1) * pageSize, currentPage * pageSize) : data);
  let columns = $derived(data.length > 0 ? Object.keys(data[0]).filter(col => !hideColumns.includes(col)) : []);

  const formatValue = (value: any, key: string) => {
    if (value === null || value === undefined) return '-';
    if (typeof value === 'boolean') {
      return value ? 'Yes' : 'No';
    }
    if (typeof value === 'object') {
      return value.name || value.originName || value.destinationName || value.scheduleName || JSON.stringify(value);
    }
    if (key.includes('Time') && typeof value === 'string') {
      return new Date(value).toLocaleString();
    }
    if (key.includes('ConStr') && !editMode) {
      return '••••••••';
    }
    return String(value);
  };

  const getColumnName = (key: string) => {
    return key.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase());
  };
</script>

{#if data.length === 0}
  <div class="text-center py-8 text-muted-foreground">
    No data available
  </div>
{:else}
  <div class="rounded-md border">
    <Table>
      <TableHeader>
        <TableRow>
          {#each columns as column}
            <TableHead class="capitalize">
              {getColumnName(column)}
            </TableHead>
          {/each}
          {#if onEdit || onDelete}
            <TableHead class="text-right">Actions</TableHead>
          {/if}
        </TableRow>
      </TableHeader>
      <TableBody>
        {#each paginatedData as item, index}
          <TableRow>
            {#each columns as column}
              <TableCell>
                {#if typeof item[column] === 'boolean'}
                  <Badge variant={item[column] ? 'default' : 'secondary'}>
                    {formatValue(item[column], column)}
                  </Badge>
                {:else}
                  {formatValue(item[column], column)}
                {/if}
              </TableCell>
            {/each}
            {#if onEdit || onDelete}
              <TableCell class="text-right">
                <div class="flex justify-end gap-2">
                  {#if onEdit}
                    <Button
                      variant="outline"
                      size="sm"
                      onclick={() => onEdit?.(item)}
                    >
                      <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                      </svg>
                    </Button>
                  {/if}
                  {#if onDelete}
                    <Button
                      variant="destructive"
                      size="sm"
                      onclick={() => onDelete?.(item)}
                    >
                      <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                      </svg>
                    </Button>
                  {/if}
                </div>
              </TableCell>
            {/if}
          </TableRow>
        {/each}
      </TableBody>
    </Table>
  </div>

  {#if showPagination && totalPages > 1}
    <div class="flex items-center justify-between px-2 py-4">
      <div class="text-sm text-muted-foreground">
        Showing {((currentPage - 1) * pageSize) + 1} to {Math.min(currentPage * pageSize, data.length)} of {data.length} entries
      </div>
      <div class="flex items-center space-x-2">
        <Button
          variant="outline"
          size="sm"
          disabled={currentPage <= 1}
          onclick={() => onPageChange?.(currentPage - 1)}
        >
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
          </svg>
          Previous
        </Button>
        
        <div class="flex items-center space-x-1">
          {#each Array.from({ length: Math.min(5, totalPages) }, (_, i) => {
            const startPage = Math.max(1, currentPage - 2);
            return startPage + i;
          }) as page}
            {#if page <= totalPages}
              <Button
                variant={page === currentPage ? 'default' : 'outline'}
                size="sm"
                onclick={() => onPageChange?.(page)}
              >
                {page}
              </Button>
            {/if}
          {/each}
        </div>
        
        <Button
          variant="outline"
          size="sm"
          disabled={currentPage >= totalPages}
          onclick={() => onPageChange?.(currentPage + 1)}
        >
          Next
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
          </svg>
        </Button>
      </div>
    </div>
  {/if}
{/if}
