<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	value?: string;
	placeholder?: string;
	rows?: number;
	cols?: number;
	disabled?: boolean;
	readonly?: boolean;
	required?: boolean;
	class?: string;
	id?: string;
	name?: string;
};

let {
	value = $bindable(''),
	placeholder,
	rows = 3,
	cols,
	disabled = false,
	readonly = false,
	required = false,
	class: className = '',
	id,
	name,
	...restProps
}: Props = $props();
</script>

<textarea
bind:value
{placeholder}
{rows}
{cols}
{disabled}
{readonly}
{required}
{id}
{name}
class={cn(
	'flex min-h-[80px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 transition-all duration-200 hover:border-accent/50 focus:border-accent resize-none',
	className
)}
{...restProps}
></textarea>
