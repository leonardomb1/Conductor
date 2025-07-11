<script lang="ts">
  interface Option {
    value: string | number;
    label: string;
    disabled?: boolean;
  }

  interface Props {
    options: Option[];
    value?: string | number;
    placeholder?: string;
    disabled?: boolean;
    required?: boolean;
    label?: string;
    error?: string;
    help?: string;
    id?: string;
  }

  let {
    options,
    value = $bindable(''),
    placeholder = 'Select an option',
    disabled = false,
    required = false,
    label,
    error,
    help,
    id,
    ...props
  }: Props = $props();

  const selectId = id || `select-${Math.random().toString(36).substr(2, 9)}`;
</script>

<div class="space-y-1">
  {#if label}
    <label for={selectId} class="block text-sm font-medium text-supabase-gray-700">
      {label}
      {#if required}
        <span class="text-red-500">*</span>
      {/if}
    </label>
  {/if}
  
  <select
    id={selectId}
    class="block w-full rounded-md border-supabase-gray-300 shadow-sm focus:border-supabase-green focus:ring-supabase-green sm:text-sm"
    class:border-red-300={error}
    class:focus:border-red-500={error}
    class:focus:ring-red-500={error}
    bind:value
    {disabled}
    {required}
    {...props}
  >
    {#if placeholder}
      <option value="" disabled>{placeholder}</option>
    {/if}
    {#each options as option}
      <option value={option.value} disabled={option.disabled}>
        {option.label}
      </option>
    {/each}
  </select>
  
  {#if error}
    <p class="text-sm text-red-600">{error}</p>
  {:else if help}
    <p class="text-sm text-supabase-gray-500">{help}</p>
  {/if}
</div>
