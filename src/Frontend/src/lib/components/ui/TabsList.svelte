<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
};

let { class: className, children, ...restProps }: Props = $props();
</script>

<div class={cn('inline-flex h-10 items-center justify-center rounded-md bg-muted p-1 text-muted-foreground shadow-sm', className)} {...restProps}>
{@render children?.()}
</div>
