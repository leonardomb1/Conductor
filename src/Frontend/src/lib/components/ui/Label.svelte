<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	for?: string;
	class?: string;
	children?: any;
};

let { for: htmlFor, class: className, children, ...restProps }: Props = $props();
</script>

<label
for={htmlFor}
class={cn('text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70 text-foreground', className)}
{...restProps}
>
{@render children?.()}
</label>
