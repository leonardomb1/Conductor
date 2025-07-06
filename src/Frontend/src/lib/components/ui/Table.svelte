<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
};

let { class: className, children, ...restProps }: Props = $props();
</script>

<div class="relative w-full overflow-auto">
<table class={cn('w-full caption-bottom text-sm', className)} {...restProps}>
	{@render children?.()}
</table>
</div>
