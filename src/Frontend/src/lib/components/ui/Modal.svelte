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
    sm: 'max-w-md',
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
    class="fixed inset-0 z-50 overflow-y-auto"
    onkeydown={handleKeydown}
  >
    <div class="flex min-h-screen items-center justify-center p-4">
      <!-- Background overlay -->
      <!-- svelte-ignore a11y_click_events_have_key_events -->
      <!-- svelte-ignore a11y_no_static_element_interactions -->
      <div 
        class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onclick={handleBackdropClick}
      ></div>
      
      <!-- Modal content -->
      <div 
        class="relative bg-white rounded-lg shadow-xl w-full {sizes[size]}"
        class:max-h-[90vh]={scrollable}
        class:flex={scrollable}
        class:flex-col={scrollable}
      >
        {#if title}
          <div class="flex items-center justify-between p-6 border-b border-supabase-gray-200 flex-shrink-0">
            <h3 class="text-lg font-semibold text-supabase-gray-900">{title}</h3>
            <button
              onclick={handleClose}
              class="text-supabase-gray-400 hover:text-supabase-gray-600 transition-colors"
            >
              <X size={20} />
            </button>
          </div>
        {/if}
        
        <div 
          class="p-6"
          class:overflow-y-auto={scrollable}
          class:flex-1={scrollable}
        >
          {@render children()}
        </div>
      </div>
    </div>
  </div>
{/if}