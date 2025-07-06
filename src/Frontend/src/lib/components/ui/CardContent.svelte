<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
};

let { class: className, children, ...restProps }: Props = $props();
</script>

<div class={cn('p-6 pt-0', className)} {...restProps}>
{@render children?.()}
</div>
