<script lang="ts">
  interface Props {
    title?: string;
    description?: string;
    children: any;
    class?: string;
  }

  let {
    title,
    description,
    children,
    class: className = '',
    ...props
  }: Props = $props();
</script>

<div class="bg-white shadow rounded-lg {className}" {...props}>
  {#if title || description}
    <div class="px-6 py-4 border-b border-supabase-gray-200">
      {#if title}
        <h3 class="text-lg font-medium text-supabase-gray-900">{title}</h3>
      {/if}
      {#if description}
        <p class="mt-1 text-sm text-supabase-gray-600">{description}</p>
      {/if}
    </div>
  {/if}
  
  <div class="px-6 py-4">
    {@render children()}
  </div>
</div>
