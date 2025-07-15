<script lang="ts">
  import { X } from '@lucide/svelte';

  interface Props {
    open: boolean;
    title?: string;
    children: any;
    onClose?: () => void;
    size?: 'sm' | 'md' | 'lg' | 'xl' | '2xl';
    scrollable?: boolean;
  }

  let {
    open = $bindable(false),
    title,
    children,
    onClose,
    size = 'md',
    scrollable = true
  }: Props = $props();

  const sizes = {
    sm: 'max-w-sm',
    md: 'max-w-lg',
    lg: 'max-w-2xl',
    xl: 'max-w-4xl',
    '2xl': 'max-w-6xl'
  };

  function handleClose() {
    open = false;
    onClose?.();
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      handleClose();
    }
  }

  function handleBackdropClick(event: MouseEvent) {
    if (event.target === event.currentTarget) {
      handleClose();
    }
  }
</script>

{#if open}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div 
    class="fixed inset-0 z-50 overflow-y-auto p-4 sm:p-6 md:p-20"
    onkeydown={handleKeydown}
  >
    <div class="flex min-h-full items-center justify-center">
      <!-- Background overlay -->
      <!-- svelte-ignore a11y_click_events_have_key_events -->
      <!-- svelte-ignore a11y_no_static_element_interactions -->
      <div 
        class="fixed inset-0 bg-black/50 backdrop-blur-sm transition-opacity modal-backdrop"
        onclick={handleBackdropClick}
      ></div>
      
      <!-- Modal content -->
      <div 
        class="relative bg-white dark:bg-gray-800 rounded-lg shadow-xl w-full mx-4 sm:mx-0 {sizes[size]} modal-content"
        class:max-h-[90vh]={scrollable}
        class:flex={scrollable}
        class:flex-col={scrollable}
      >
        {#if title}
          <div class="flex items-center justify-between p-4 sm:p-6 border-b border-gray-200 dark:border-gray-700 flex-shrink-0">
            <h3 class="text-lg font-semibold text-gray-900 dark:text-white">{title}</h3>
            <button
              onclick={handleClose}
              class="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 transition-colors p-1 rounded-md hover:bg-gray-100 dark:hover:bg-gray-700"
            >
              <X size={20} />
            </button>
          </div>
        {:else}
          <!-- Close button for modals without titles -->
          <div class="absolute top-4 right-4 z-10">
            <button
              onclick={handleClose}
              class="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 transition-colors p-2 rounded-full hover:bg-gray-100 dark:hover:bg-gray-700"
            >
              <X size={20} />
            </button>
          </div>
        {/if}
        
        <div 
          class="p-4 sm:p-6"
          class:overflow-y-auto={scrollable}
          class:flex-1={scrollable}
        >
          {@render children()}
        </div>
      </div>
    </div>
  </div>
{/if}