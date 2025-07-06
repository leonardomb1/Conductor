<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	type?: string;
	value?: string | number;
	placeholder?: string;
	disabled?: boolean;
	required?: boolean;
	min?: string | number;
	max?: string | number;
	step?: string | number;
	oninput?: (e: Event) => void;
	class?: string;
	id?: string;
};

let { type = 'text', value = $bindable(), placeholder, disabled = false, required = false, min, max, step, oninput, class: className, id, ...restProps }: Props = $props();
</script>

<input
{type}
{id}
bind:value
{placeholder}
{disabled}
{required}
{min}
{max}
{step}
oninput={oninput}
class={cn(
	'flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background file:border-0 file:bg-transparent file:text-sm file:font-medium placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 transition-all duration-200 hover:border-accent/50 focus:border-accent',
	className
)}
{...restProps}
/>
