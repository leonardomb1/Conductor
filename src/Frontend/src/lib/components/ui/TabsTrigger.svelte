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
	setValue: (value: string) => void;
};

const isActive = derived(tabs.value, ($value) => $value === value);
</script>

<button
type="button"
role="tab"
aria-selected={$isActive}
data-state={$isActive ? 'active' : 'inactive'}
class={cn(
	'inline-flex items-center justify-center whitespace-nowrap rounded-sm px-3 py-1.5 text-sm font-medium ring-offset-background transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50',
	$isActive ? 'bg-background text-foreground shadow-sm' : 'hover:bg-background/50 hover:text-foreground',
	className
)}
onclick={() => tabs.setValue(value)}
{...restProps}
>
{@render children?.()}
</button>
