<script lang="ts">
  import { CheckCircle, XCircle, Info, X } from "@lucide/svelte"

  interface Props {
    show: boolean
    type?: "success" | "error" | "info" | "warning"
    message: string
    duration?: number
    onClose?: () => void
  }

  let {
    show = $bindable(false),
    type = "info",
    message,
    duration = 5000,
    onClose,
  }: Props = $props()

  let timeoutId: number | null = null

  const typeConfig = {
    success: {
      icon: CheckCircle,
      bgColor: "bg-green-50 border-green-200",
      textColor: "text-green-800",
      iconColor: "text-green-500",
    },
    error: {
      icon: XCircle,
      bgColor: "bg-red-50 border-red-200",
      textColor: "text-red-800",
      iconColor: "text-red-500",
    },
    warning: {
      icon: Info,
      bgColor: "bg-yellow-50 border-yellow-200",
      textColor: "text-yellow-800",
      iconColor: "text-yellow-500",
    },
    info: {
      icon: Info,
      bgColor: "bg-blue-50 border-blue-200",
      textColor: "text-blue-800",
      iconColor: "text-blue-500",
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
  <div
    class="fixed top-4 right-4 z-50 w-96 transform transition-all duration-300 ease-in-out"
  >
    <div class="rounded-lg border p-4 shadow-lg {typeConfig[type].bgColor}">
      <div class="flex items-start">
        <div class="flex-shrink-0">
          <IconComponent class="h-5 w-5 {typeConfig[type].iconColor}" />
        </div>
        <div class="ml-3 w-0 flex-1">
          <p class="text-sm font-medium {typeConfig[type].textColor}">
            {message}
          </p>
        </div>
        <div class="ml-4 flex flex-shrink-0">
          <button
            onclick={handleClose}
            class="inline-flex rounded-md {typeConfig[type]
              .bgColor} {typeConfig[type]
              .textColor} hover:opacity-75 focus:outline-none focus:ring-2 focus:ring-offset-2"
          >
            <span class="sr-only">Close</span>
            <X class="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  </div>
{/if}
