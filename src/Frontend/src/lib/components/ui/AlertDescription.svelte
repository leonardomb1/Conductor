<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	class?: string;
	children?: any;
};

let { class: className, children, ...restProps }: Props = $props();
</script>

<div class={cn('text-sm [&_p]:leading-relaxed', className)} {...restProps}>
{@render children?.()}
</div>
