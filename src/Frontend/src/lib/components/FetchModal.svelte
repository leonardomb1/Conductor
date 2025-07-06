<script lang="ts">
  import { Button } from '$lib/components/ui/Button.svelte';
  import { Input } from '$lib/components/ui/Input.svelte';
  import { Label } from '$lib/components/ui/Label.svelte';
  import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from '$lib/components/ui/Dialog.svelte';
  import { Alert, AlertDescription } from '$lib/components/ui/Alert.svelte';
  import { Badge } from '$lib/components/ui/Badge.svelte';
  import { Separator } from '$lib/components/ui/Separator.svelte';
  import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '$lib/components/ui/Table.svelte';
  import { Card, CardContent } from '$lib/components/ui/Card.svelte';
  import { AuthService } from '$lib/auth';
  import type { FetchResult } from '$lib/types';

  interface Props {
    isOpen?: boolean;
    onClose: () => void;
  }

  let { isOpen = false, onClose }: Props = $props();

  interface FetchParams {
    name?: string;
    contains?: string;
    schedule?: string;
    scheduleId?: string;
    origin?: string;
    destination?: string;
    take?: string;
    overrideTime?: string;
    page?: string;
    pageSize?: string;
  }

  let params: FetchParams = $state({
    name: '',
    contains: '',
    schedule: '',
    scheduleId: '',
    origin: '',
    destination: '',
    take: '100',
    overrideTime: '',
    page: '1',
    pageSize: '50',
  });
  
  let loading = $state(false);
  let error = $state('');
  let result: FetchResult | null = $state(null);
  let showPreview = $state(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setLoading(true);
    setError('');

    try {
      // Filter out empty parameters
      const filteredParams: Record<string, string> = {};
      Object.entries(params).forEach(([key, value]) => {
        if (value && value.trim()) {
          filteredParams[key] = value.trim();
        }
      });

      // Build query string properly
      const queryParams = new URLSearchParams(filteredParams);
      const url = `/api/extractions/fetch?${queryParams.toString()}`;

      const response = await AuthService.authenticatedFetch(url);
      if (!response.ok) throw new Error('Failed to fetch data');

      const responseData = await response.json();

      // Process the response based on your API structure
      const processedResult: FetchResult = {
        data: responseData.content || responseData.data || [],
        totalCount: responseData.totalCount || responseData.entityCount || 0,
        page: parseInt(filteredParams.page || '1'),
        pageSize: parseInt(filteredParams.pageSize || '50'),
        hasNextPage: responseData.hasNextPage || false,
        hasPreviousPage: responseData.hasPreviousPage || false,
      };

      result = processedResult;
      showPreview = true;
    } catch (err) {
      error = err instanceof Error ? err.message : 'Fetch failed';
    } finally {
      loading = false;
    }
  };

  const handlePageChange = async (newPage: number) => {
    const newParams = { ...params, page: newPage.toString() };
    params = newParams;

    // Auto-fetch with new page
    loading = true;
    try {
      const filteredParams: Record<string, string> = {};
      Object.entries(newParams).forEach(([key, value]) => {
        if (value && value.trim()) {
          filteredParams[key] = value.trim();
        }
      });

      const queryParams = new URLSearchParams(filteredParams);
      const url = `/api/extractions/fetch?${queryParams.toString()}`;

      const response = await AuthService.authenticatedFetch(url);
      if (!response.ok) throw new Error('Failed to fetch data');

      const responseData = await response.json();
      const processedResult: FetchResult = {
        data: responseData.content || responseData.data || [],
        totalCount: responseData.totalCount || responseData.entityCount || 0,
        page: newPage,
        pageSize: parseInt(newParams.pageSize || '50'),
        hasNextPage: responseData.hasNextPage || false,
        hasPreviousPage: responseData.hasPreviousPage || false,
      };

      result = processedResult;
    } catch (err) {
      error = err instanceof Error ? err.message : 'Fetch failed';
    } finally {
      loading = false;
    }
  };

  const exportToCSV = () => {
    if (!result || !result.data.length) return;

    const headers = Object.keys(result.data[0]);
    const csvContent = [
      headers.join(','),
      ...result.data.map((row) =>
        headers
          .map((header) => {
            const value = row[header];
            // Escape commas and quotes in CSV
            if (typeof value === 'string' && (value.includes(',') || value.includes('"'))) {
              return `"${value.replace(/"/g, '""')}"`;
            }
            return value || '';
          })
          .join(','),
      ),
    ].join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `fetch-results-${new Date().toISOString().split('T')[0]}.csv`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    window.URL.revokeObjectURL(url);
  };

  const resetForm = () => {
    params = {
      name: '',
      contains: '',
      schedule: '',
      scheduleId: '',
      origin: '',
      destination: '',
      take: '100',
      overrideTime: '',
      page: '1',
      pageSize: '50',
    };
    result = null;
    showPreview = false;
    error = '';
  };

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
    return String(value);
  };

  function setLoading(value: boolean) {
    loading = value;
  }

  function setError(value: string) {
    error = value;
  }
</script>

<Dialog open={isOpen} onOpenChange={onClose}>
  <DialogContent class="max-w-6xl max-h-[90vh] overflow-y-auto">
    <DialogHeader>
      <DialogTitle class="flex items-center gap-2">
        <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
        </svg>
        Fetch Data Preview
      </DialogTitle>
    </DialogHeader>

    <div class="space-y-4">
      <Alert>
        <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
        <AlertDescription>
          Fetch and preview data from extractions without executing a full job. Use filters to narrow down results.
        </AlertDescription>
      </Alert>

      {#if error}
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      {/if}

      {#if !showPreview}
        <Separator />
        <form onsubmit={handleSubmit} class="space-y-4">
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div class="space-y-2">
              <Label for="name">
                Extraction Name
                <Badge variant="outline" class="ml-2 text-xs">exact match</Badge>
              </Label>
              <Input
                id="name"
                bind:value={params.name}
                placeholder="Filter by exact extraction name"
              />
            </div>

            <div class="space-y-2">
              <Label for="contains">
                Contains
                <Badge variant="outline" class="ml-2 text-xs">comma-separated</Badge>
              </Label>
              <Input
                id="contains"
                bind:value={params.contains}
                placeholder="Values to search for"
              />
            </div>

            <div class="space-y-2">
              <Label for="schedule">
                Schedule Name
                <Badge variant="outline" class="ml-2 text-xs">exact match</Badge>
              </Label>
              <Input
                id="schedule"
                bind:value={params.schedule}
                placeholder="Filter by schedule name"
              />
            </div>

            <div class="space-y-2">
              <Label for="scheduleId">
                Schedule ID
                <Badge variant="outline" class="ml-2 text-xs">number</Badge>
              </Label>
              <Input
                id="scheduleId"
                type="number"
                bind:value={params.scheduleId}
                placeholder="Filter by schedule ID"
              />
            </div>

            <div class="space-y-2">
              <Label for="origin">
                Origin Name
                <Badge variant="outline" class="ml-2 text-xs">exact match</Badge>
              </Label>
              <Input
                id="origin"
                bind:value={params.origin}
                placeholder="Filter by origin name"
              />
            </div>

            <div class="space-y-2">
              <Label for="destination">
                Destination Name
                <Badge variant="outline" class="ml-2 text-xs">exact match</Badge>
              </Label>
              <Input
                id="destination"
                bind:value={params.destination}
                placeholder="Filter by destination name"
              />
            </div>

            <div class="space-y-2">
              <Label for="take">
                Max Records
                <Badge variant="outline" class="ml-2 text-xs">limit</Badge>
              </Label>
              <Input
                id="take"
                type="number"
                bind:value={params.take}
                placeholder="Limit number of records"
              />
            </div>

            <div class="space-y-2">
              <Label for="overrideTime">
                Override Time
                <Badge variant="outline" class="ml-2 text-xs">milliseconds</Badge>
              </Label>
              <Input
                id="overrideTime"
                type="number"
                bind:value={params.overrideTime}
                placeholder="Override filter time (ms)"
              />
            </div>

            <div class="space-y-2">
              <Label for="pageSize">
                Page Size
                <Badge variant="outline" class="ml-2 text-xs">per page</Badge>
              </Label>
              <Input
                id="pageSize"
                type="number"
                bind:value={params.pageSize}
                placeholder="Records per page"
                min="1"
                max="1000"
              />
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onclick={onClose} disabled={loading}>
              Cancel
            </Button>
            <Button type="submit" disabled={loading}>
              {#if loading}
                <div class="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                Fetching...
              {:else}
                <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                Fetch Data
              {/if}
            </Button>
          </DialogFooter>
        </form>
      {/if}

      {#if showPreview && result}
        <div class="space-y-4">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-4">
              <h3 class="text-lg font-semibold">Data Preview</h3>
              <Badge variant="outline">
                {result.data.length} of {result.totalCount} records
              </Badge>
            </div>
            <div class="flex gap-2">
              <Button onclick={exportToCSV} variant="outline" size="sm">
                <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                </svg>
                Export CSV
              </Button>
              <Button onclick={resetForm} variant="outline" size="sm">
                New Search
              </Button>
            </div>
          </div>

          <Card>
            <CardContent class="p-0">
              {#if result.data.length > 0}
                <div class="rounded-md border">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        {#each Object.keys(result.data[0]) as column}
                          <TableHead class="capitalize">
                            {column.replace(/([A-Z])/g, ' $1').trim()}
                          </TableHead>
                        {/each}
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {#each result.data as item, index}
                        <TableRow>
                          {#each Object.keys(result.data[0]) as column}
                            <TableCell>{formatValue(item[column], column)}</TableCell>
                          {/each}
                        </TableRow>
                      {/each}
                    </TableBody>
                  </Table>
                </div>
              {:else}
                <div class="text-center py-8 text-muted-foreground">
                  No data found with the current filters
                </div>
              {/if}
            </CardContent>
          </Card>

          {#if result.totalCount > result.pageSize}
            <div class="flex items-center justify-between">
              <div class="text-sm text-muted-foreground">
                Page {result.page} of {Math.ceil(result.totalCount / result.pageSize)}
              </div>
              <div class="flex gap-2">
                <Button
                  onclick={() => handlePageChange(result.page - 1)}
                  disabled={!result.hasPreviousPage || loading}
                  variant="outline"
                  size="sm"
                >
                  <svg class="h-4 w-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
                  </svg>
                  Previous
                </Button>
                <Button
                  onclick={() => handlePageChange(result.page + 1)}
                  disabled={!result.hasNextPage || loading}
                  variant="outline"
                  size="sm"
                >
                  Next
                  <svg class="h-4 w-4 ml-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
                  </svg>
                </Button>
              </div>
            </div>
          {/if}
        </div>
      {/if}
    </div>
  </DialogContent>
</Dialog>
