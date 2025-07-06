<script lang="ts">
  import { toasts } from "$lib/stores/toast"
  import Toast from "$lib/components/ui/Toast.svelte"
  import { CheckCircle, XCircle, AlertTriangle, Info } from "lucide-svelte"
  import type { Component } from "svelte"

  function getIcon(type: string): Component {
    switch (type) {
      case "success":
        return CheckCircle
      case "error":
        return XCircle
      case "warning":
        return AlertTriangle
      case "info":
        return Info
      default:
        return Info
    }
  }

  function getVariant(type: string): string {
    switch (type) {
      case "error":
        return "destructive"
      case "success":
        return "default"
      case "warning":
        return "default"
      case "info":
        return "default"
      default:
        return "default"
    }
  }
</script>

<div class="fixed top-4 right-4 z-50 w-full max-w-sm space-y-2">
  {#each $toasts as toast (toast.id)}
    <Toast
      variant={getVariant(toast.type)}
      onclose={() => toasts.remove(toast.id)}
    >
      <div class="flex items-start gap-3">
        {@render getIcon(toast.type)({ class: "h-5 w-5 mt-0.5 flex-shrink-0" })}
        <div class="flex-1">
          <div class="font-medium">{toast.title}</div>
          {#if toast.description}
            <div class="text-sm opacity-90">{toast.description}</div>
          {/if}
        </div>
      </div>
    </Toast>
  {/each}
</div>
