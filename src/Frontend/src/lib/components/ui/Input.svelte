<script lang="ts">
  interface Props {
    type?: string;
    placeholder?: string;
    value?: string;
    disabled?: boolean;
    required?: boolean;
    label?: string;
    error?: string;
    help?: string;
    id?: string;
    size?: 'sm' | 'md' | 'lg';
  }

  let {
    type = 'text',
    placeholder,
    value = $bindable(''),
    disabled = false,
    required = false,
    label,
    error,
    help,
    id,
    size = 'md',
    ...props
  }: Props = $props();

  const inputId = id || `input-${Math.random().toString(36).substr(2, 9)}`;
  
  const sizeClasses = {
    sm: 'text-sm px-3 py-1.5',
    md: 'text-sm px-3 py-2',
    lg: 'text-base px-4 py-3'
  };
</script>

<div class="space-y-1">
  {#if label}
    <label for={inputId} class="block text-sm font-medium text-gray-700 dark:text-gray-300">
      {label}
      {#if required}
        <span class="text-red-500">*</span>
      {/if}
    </label>
  {/if}
  
  <input
    {type}
    id={inputId}
    class="block w-full rounded-md border-gray-300 dark:border-gray-600 dark:bg-gray-800 dark:text-white shadow-sm focus:border-supabase-green focus:ring-supabase-green transition-colors {sizeClasses[size]}"
    class:border-red-300={error}
    class:dark:border-red-500={error}
    class:focus:border-red-500={error}
    class:focus:ring-red-500={error}
    {placeholder}
    bind:value
    {disabled}
    {required}
    {...props}
  />
  
  {#if error}
    <p class="text-sm text-red-600 dark:text-red-400">{error}</p>
  {:else if help}
    <p class="text-sm text-gray-500 dark:text-gray-400">{help}</p>
  {/if}
</div>