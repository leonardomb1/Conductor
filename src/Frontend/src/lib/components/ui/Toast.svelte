<script lang="ts">
  import { CheckCircle, XCircle, Info, X } from "@lucide/svelte"

  interface Props {
    show: boolean
    type?: "success" | "error" | "info" | "warning"
    message: string
    duration?: number
    allowHtml?: boolean
    onClose?: () => void
  }

  let {
    show = $bindable(false),
    type = "info",
    message,
    duration = 5000,
    allowHtml = false,
    onClose,
  }: Props = $props()

  let timeoutId: number | null = null

  const typeConfig = {
    success: {
      icon: CheckCircle,
      bgColor: "bg-green-50 dark:bg-green-900/20 border-green-200 dark:border-green-800",
      textColor: "text-green-800 dark:text-green-300",
      iconColor: "text-green-500 dark:text-green-400",
    },
    error: {
      icon: XCircle,
      bgColor: "bg-red-50 dark:bg-red-900/20 border-red-200 dark:border-red-800",
      textColor: "text-red-800 dark:text-red-300",
      iconColor: "text-red-500 dark:text-red-400",
    },
    warning: {
      icon: Info,
      bgColor: "bg-yellow-50 dark:bg-yellow-900/20 border-yellow-200 dark:border-yellow-800",
      textColor: "text-yellow-800 dark:text-yellow-300",
      iconColor: "text-yellow-500 dark:text-yellow-400",
    },
    info: {
      icon: Info,
      bgColor: "bg-blue-50 dark:bg-blue-900/20 border-blue-200 dark:border-blue-800",
      textColor: "text-blue-800 dark:text-blue-300",
      iconColor: "text-blue-500 dark:text-blue-400",
    },
  }

  function handleClose() {
    show = false
    onClose?.()
    if (timeoutId) {
      clearTimeout(timeoutId)
    }
  }

  $effect(() => {
    if (show && duration > 0) {
      timeoutId = setTimeout(() => {
        handleClose()
      }, duration)
    }

    return () => {
      if (timeoutId) {
        clearTimeout(timeoutId)
      }
    }
  })

  // Get the icon component dynamically
  const IconComponent = $derived(typeConfig[type].icon)
</script>

{#if show}
  <div class="fixed top-4 right-4 z-60 w-96 max-w-[calc(100vw-2rem)] transform transition-all duration-300 ease-in-out toast-container">
    <div class="rounded-lg border p-4 shadow-lg {typeConfig[type].bgColor}">
      <div class="flex items-start">
        <div class="flex-shrink-0">
          <IconComponent class="h-5 w-5 {typeConfig[type].iconColor}" />
        </div>
        <div class="ml-3 w-0 flex-1">
          {#if allowHtml}
            <div class="text-sm font-medium {typeConfig[type].textColor}">
              {@html message}
            </div>
          {:else}
            <p class="text-sm font-medium {typeConfig[type].textColor}">
              {message}
            </p>
          {/if}
        </div>
        <div class="ml-4 flex flex-shrink-0">
          <button
            onclick={handleClose}
            class="inline-flex rounded-md {typeConfig[type].bgColor} {typeConfig[type].textColor} hover:opacity-75 focus:outline-none focus:ring-2 focus:ring-offset-2 transition-opacity"
          >
            <span class="sr-only">Close</span>
            <X class="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  </div>
{/if}

<style>
  /* Enhanced styling for HTML content in toasts */
  :global(.toast-content .job-guid-highlight) {
    background-color: #fef3c7;
    padding: 2px 6px;
    border-radius: 4px;
    font-weight: 600;
    color: #92400e;
    border: 1px solid #fbbf24;
    cursor: pointer;
    transition: all 0.2s ease-in-out;
  }
  
  :global(.dark .toast-content .job-guid-highlight) {
    background-color: #451a03;
    color: #fed7aa;
    border-color: #92400e;
  }
  
  :global(.toast-content .job-guid-highlight:hover) {
    background-color: #fed7aa;
    border-color: #f97316;
    transform: scale(1.02);
  }
  
  :global(.dark .toast-content .job-guid-highlight:hover) {
    background-color: #7c2d12;
    border-color: #ea580c;
  }
  
  :global(.toast-content small) {
    font-size: 0.75rem;
    opacity: 0.8;
    font-style: italic;
  }

  /* Mobile responsiveness */
  @media (max-width: 640px) {
    .toast-container {
      width: calc(100vw - 2rem);
      right: 1rem;
      left: 1rem;
    }
  }
</style>