<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	metallic?: boolean;
};

let { class: className, metallic = false, ...restProps }: Props = $props();
</script>

<div
class={cn(
	'rounded-md',
	metallic ? 'animate-metallic-shimmer' : 'animate-pulse bg-muted',
	className
)}
{...restProps}
></div>
