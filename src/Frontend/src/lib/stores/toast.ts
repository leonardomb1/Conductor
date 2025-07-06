import { writable } from "svelte/store"

export interface Toast {
  id: string
  type: "success" | "error" | "warning" | "info"
  title: string
  description?: string
  duration?: number
}

function createToastStore() {
  const { subscribe, update } = writable<Toast[]>([])

  return {
    subscribe,
    add: (toast: Omit<Toast, "id">) => {
      const id = Math.random().toString(36).substr(2, 9)
      const newToast: Toast = {
        id,
        duration: 5000,
        ...toast,
      }

      update((toasts) => [...toasts, newToast])

      if (newToast.duration && newToast.duration > 0) {
        setTimeout(() => {
          update((toasts) => toasts.filter((t) => t.id !== id))
        }, newToast.duration)
      }

      return id
    },
    remove: (id: string) => {
      update((toasts) => toasts.filter((t) => t.id !== id))
    },
    clear: () => {
      update(() => [])
    },
  }
}

export const toasts = createToastStore()
