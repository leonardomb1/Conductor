<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
};

let { class: className, children, ...restProps }: Props = $props();
</script>

<td class={cn('p-4 align-middle [&:has([role=checkbox])]:pr-0', className)} {...restProps}>
{@render children?.()}
</td>
