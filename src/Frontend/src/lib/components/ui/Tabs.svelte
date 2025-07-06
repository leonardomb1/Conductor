<script lang="ts">
import { setContext } from 'svelte';

type Props = {
	value?: string;
	onValueChange?: (value: string) => void;
	children?: any;
};

let { value = $bindable(), onValueChange, children, ...restProps }: Props = $props();

setContext('tabs', {
	value: () => value,
	setValue: (newValue: string) => {
		value = newValue;
		onValueChange?.(newValue);
	}
});
</script>

<div data-orientation="horizontal" role="tablist" {...restProps}>
{@render children?.()}
</div>
