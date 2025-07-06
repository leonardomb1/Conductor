<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
	metallic?: boolean;
};

let { class: className, children, metallic = false, ...restProps }: Props = $props();
</script>

<h3 class={cn(
	'text-2xl font-semibold leading-none tracking-tight',
	metallic && 'metallic-text-gradient',
	className
)} {...restProps}>
{@render children?.()}
</h3>
