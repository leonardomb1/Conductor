<script lang="ts">
  interface Props {
    title: string;
    description?: string;
    actions?: any;
  }

  let {
    title,
    description,
    actions
  }: Props = $props();
</script>

<div class="border-b border-gray-200 dark:border-gray-700 pb-5 sm:flex sm:items-center sm:justify-between">
  <div class="min-w-0 flex-1">
    <h1 class="text-2xl font-semibold text-gray-900 dark:text-white truncate">{title}</h1>
    {#if description}
      <p class="mt-2 text-sm text-gray-600 dark:text-gray-400">{description}</p>
    {/if}
  </div>
  
  {#if actions}
    <div class="mt-4 sm:mt-0 sm:ml-4 flex-shrink-0">
      <div class="flex flex-col sm:flex-row gap-2 sm:gap-3">
        {@render actions()}
      </div>
    </div>
  {/if}
</div>