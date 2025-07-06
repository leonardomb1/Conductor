<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
};

let { class: className, children, ...restProps }: Props = $props();
</script>

<div class={cn('grid gap-4', className)} {...restProps}>
{@render children?.()}
</div>
