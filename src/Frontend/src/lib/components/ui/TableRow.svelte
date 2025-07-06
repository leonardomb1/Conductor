<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
};

let { class: className, children, ...restProps }: Props = $props();
</script>

<tr class={cn('border-b transition-colors hover:bg-muted/50 data-[state=selected]:bg-muted', className)} {...restProps}>
{@render children?.()}
</tr>
