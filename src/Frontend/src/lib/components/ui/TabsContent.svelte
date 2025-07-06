<script lang="ts">
import { cn } from '$lib/utils';
import { getContext } from 'svelte';
import { derived } from 'svelte/store';

type Props = {
	value: string;
	class?: string;
	children?: any;
};

let { value, class: className, children, ...restProps }: Props = $props();

const tabs = getContext('tabs') as {
	value: () => string;
};

const isActive = derived(tabs.value, ($value) => $value === value);
</script>

{#if $isActive}
<div
	role="tabpanel"
	data-state="active"
	class={cn('mt-2 ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2', className)}
	{...restProps}
>
	{@render children?.()}
</div>
{/if}
