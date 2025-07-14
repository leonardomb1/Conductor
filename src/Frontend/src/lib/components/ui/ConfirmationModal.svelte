<script lang="ts">
  import { X, AlertTriangle, CheckCircle, XCircle } from "@lucide/svelte"

  interface Props {
    open: boolean
    title?: string
    message: string
    type?: "danger" | "warning" | "info"
    confirmText?: string
    cancelText?: string
    loading?: boolean
    onConfirm?: () => void | Promise<void>
    onCancel?: () => void
  }

  let {
    open = $bindable(false),
    title,
    message,
    type = "danger",
    confirmText = "Confirm",
    cancelText = "Cancel",
    loading = false,
    onConfirm,
    onCancel,
  }: Props = $props()

  const typeConfig = {
    danger: {
      icon: XCircle,
      iconColor: "text-red-500",
      confirmButtonClass: "bg-red-600 hover:bg-red-700 focus:ring-red-500",
      defaultTitle: "Confirm Action",
    },
    warning: {
      icon: AlertTriangle,
      iconColor: "text-yellow-500",
      confirmButtonClass:
        "bg-yellow-600 hover:bg-yellow-700 focus:ring-yellow-500",
      defaultTitle: "Warning",
    },
    info: {
      icon: CheckCircle,
      iconColor: "text-blue-500",
      confirmButtonClass: "bg-blue-600 hover:bg-blue-700 focus:ring-blue-500",
      defaultTitle: "Confirm",
    },
  }

  const config = $derived(typeConfig[type])
  const IconComponent = $derived(config.icon)
  const modalTitle = $derived(title || config.defaultTitle)

  function handleCancel() {
    open = false
    onCancel?.()
  }

  async function handleConfirm() {
    await onConfirm?.()
    open = false
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === "Escape") {
      handleCancel()
    }
  }

  function handleBackdropClick(event: MouseEvent) {
    if (event.target === event.currentTarget) {
      handleCancel()
    }
  }
</script>

{#if open}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="fixed inset-0 z-50 overflow-y-auto" onkeydown={handleKeydown}>
    <div class="flex min-h-screen items-center justify-center p-4">
      <!-- Background overlay -->
      <!-- svelte-ignore a11y_click_events_have_key_events -->
      <!-- svelte-ignore a11y_no_static_element_interactions -->
      <div
        class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onclick={handleBackdropClick}
      ></div>

      <!-- Modal content -->
      <div class="relative bg-white rounded-lg shadow-xl w-full max-w-md">
        <div class="p-6">
          <div class="flex items-start">
            <div class="flex-shrink-0">
              <IconComponent class="h-6 w-6 {config.iconColor}" />
            </div>
            <div class="ml-3 w-0 flex-1">
              <h3 class="text-lg font-medium text-supabase-gray-900 mb-2">
                {modalTitle}
              </h3>
              <p class="text-sm text-supabase-gray-600">
                {message}
              </p>
            </div>
          </div>

          <div class="mt-6 flex justify-end space-x-3">
            <button
              onclick={handleCancel}
              disabled={loading}
              class="px-4 py-2 text-sm font-medium text-supabase-gray-700 bg-white border border-supabase-gray-300 rounded-md hover:bg-supabase-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-supabase-gray-500 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {cancelText}
            </button>
            <button
              onclick={handleConfirm}
              disabled={loading}
              class="inline-flex items-center px-4 py-2 text-sm font-medium text-white border border-transparent rounded-md focus:outline-none focus:ring-2 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed {config.confirmButtonClass}"
            >
              {#if loading}
                <svg
                  class="animate-spin -ml-1 mr-2 h-4 w-4 text-white"
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
              {/if}
              {confirmText}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
{/if}
