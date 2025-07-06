<script lang="ts">
import { cn } from '$lib/utils';
import { derived } from 'svelte/store';

type Props = {
	value?: number;
	max?: number;
	class?: string;
	metallic?: boolean;
};

let { value = 0, max = 100, class: className, metallic = false, ...restProps }: Props = $props();

const percentage = derived([value, max], ([$value, $max]) => Math.min(Math.max(($value / $max) * 100, 0), 100));
</script>

<div
class={cn(
	'relative h-4 w-full overflow-hidden rounded-full bg-secondary',
	className
)}
{...restProps}
>
<div
	class={cn(
		'h-full w-full flex-1 transition-all duration-300 ease-in-out',
		metallic ? 'bg-metallic-gradient shadow-metallic' : 'bg-primary'
	)}
	style="transform: translateX(-{100 - $percentage}%)"
></div>
</div>
