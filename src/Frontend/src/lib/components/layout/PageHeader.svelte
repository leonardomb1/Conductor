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

<div class="border-b border-supabase-gray-200 pb-5 sm:flex sm:items-center sm:justify-between">
  <div>
    <h1 class="text-2xl font-semibold text-supabase-gray-900">{title}</h1>
    {#if description}
      <p class="mt-2 text-sm text-supabase-gray-600">{description}</p>
    {/if}
  </div>
  
  {#if actions}
    <div class="mt-4 sm:mt-0">
      {@render actions()}
    </div>
  {/if}
</div>
