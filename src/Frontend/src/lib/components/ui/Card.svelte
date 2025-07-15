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

<div class="bg-white dark:bg-gray-800 shadow-sm dark:shadow-gray-900/20 rounded-lg border border-gray-200 dark:border-gray-700 {className}" {...props}>
  {#if title || description}
    <div class="px-4 sm:px-6 py-4 border-b border-gray-200 dark:border-gray-700">
      {#if title}
        <h3 class="text-lg font-medium text-gray-900 dark:text-white">{title}</h3>
      {/if}
      {#if description}
        <p class="mt-1 text-sm text-gray-600 dark:text-gray-400">{description}</p>
      {/if}
    </div>
  {/if}
  
  <div class="px-4 sm:px-6 py-4">
    {@render children()}
  </div>
</div>