<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
};

let { class: className, children, ...restProps }: Props = $props();
</script>

<thead class={cn('[&_tr]:border-b bg-muted/50', className)} {...restProps}>
{@render children?.()}
</thead>
