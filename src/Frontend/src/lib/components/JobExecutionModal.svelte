<script lang="ts">
  import Dialog from '$lib/components/ui/Dialog.svelte'
  import DialogContent from '$lib/components/ui/DialogContent.svelte'
  import DialogHeader from '$lib/components/ui/DialogHeader.svelte'
  import DialogTitle from '$lib/components/ui/DialogTitle.svelte'
  import DialogFooter from '$lib/components/ui/DialogFooter.svelte'
  import Button from '$lib/components/ui/Button.svelte'
  import Badge from '$lib/components/ui/Badge.svelte'
  import Separator from '$lib/components/ui/Separator.svelte'
  import type { JobExecution } from '$lib/types'
  
  interface Props {
    open?: boolean;
    job?: JobExecution | null;
    onClose?: () => void;
  }

  let { open = $bindable(false), job = null, onClose = () => {} }: Props = $props();
  
  function handleClose() {
    open = false
    onClose()
  }
  
  function formatDuration(duration?: number): string {
    if (!duration) return 'N/A'
    const seconds = Math.floor(duration / 1000)
    const minutes = Math.floor(seconds / 60)
    const hours = Math.floor(minutes / 60)
    
    if (hours > 0) {
      return `${hours}h ${minutes % 60}m ${seconds % 60}s`
    } else if (minutes > 0) {
      return `${minutes}m ${seconds % 60}s`
    } else {
      return `${seconds}s`
    }
  }
  
  function getStatusVariant(status: string) {
    switch (status) {
      case 'completed': return 'default'
      case 'running': return 'secondary'
      case 'failed': return 'destructive'
      case 'cancelled': return 'outline'
      default: return 'outline'
    }
  }
</script>

<Dialog bind:open>
  <DialogContent class="max-w-2xl">
    <DialogHeader>
      <DialogTitle>Job Execution Details</DialogTitle>
    </DialogHeader>
    
    {#if job}
      <div class="space-y-4">
        <div class="grid grid-cols-2 gap-4">
          <div>
            <h4 class="font-medium text-sm text-muted-foreground">Status</h4>
            <Badge variant={getStatusVariant(job.status)} class="mt-1">
              {job.status.toUpperCase()}
            </Badge>
          </div>
          <div>
            <h4 class="font-medium text-sm text-muted-foreground">Duration</h4>
            <p class="mt-1">{formatDuration(job.duration)}</p>
          </div>
        </div>
        
        <div class="grid grid-cols-2 gap-4">
          <div>
            <h4 class="font-medium text-sm text-muted-foreground">Start Time</h4>
            <p class="mt-1">{new Date(job.startTime).toLocaleString()}</p>
          </div>
          <div>
            <h4 class="font-medium text-sm text-muted-foreground">End Time</h4>
            <p class="mt-1">
              {job.endTime ? new Date(job.endTime).toLocaleString() : 'N/A'}
            </p>
          </div>
        </div>
        
        {#if job.recordsProcessed !== undefined}
          <div>
            <h4 class="font-medium text-sm text-muted-foreground">Records Processed</h4>
            <p class="mt-1">{job.recordsProcessed.toLocaleString()}</p>
          </div>
        {/if}
        
        {#if job.extraction}
          <div>
            <h4 class="font-medium text-sm text-muted-foreground">Extraction</h4>
            <p class="mt-1">{job.extraction.name}</p>
          </div>
        {/if}
        
        {#if job.schedule}
          <div>
            <h4 class="font-medium text-sm text-muted-foreground">Schedule</h4>
            <p class="mt-1">{job.schedule.name}</p>
          </div>
        {/if}
        
        {#if job.errorMessage}
          <div>
            <h4 class="font-medium text-sm text-muted-foreground text-red-600">Error Message</h4>
            <div class="mt-1 p-3 bg-red-50 border border-red-200 rounded-md">
              <p class="text-sm text-red-800">{job.errorMessage}</p>
            </div>
          </div>
        {/if}
        
        {#if job.logs}
          <div>
            <h4 class="font-medium text-sm text-muted-foreground">Logs</h4>
            <div class="mt-1 p-3 bg-gray-50 border rounded-md max-h-40 overflow-y-auto">
              <pre class="text-xs text-gray-800 whitespace-pre-wrap">{job.logs}</pre>
            </div>
          </div>
        {/if}
      </div>
    {/if}
    
    <DialogFooter>
      <Button variant="outline" onclick={handleClose}>Close</Button>
    </DialogFooter>
  </DialogContent>
</Dialog>
