<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
	metallic?: boolean;
};

let { class: className, children, metallic = false, ...restProps }: Props = $props();
</script>

<div class={cn(
	'rounded-lg border bg-card text-card-foreground shadow-sm transition-all duration-200',
	metallic && 'metallic-card shadow-metallic hover:shadow-metallic-lg',
	className
)} {...restProps}>
{@render children?.()}
</div>
