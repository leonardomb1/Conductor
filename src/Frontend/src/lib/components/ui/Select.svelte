<script lang="ts">
import { cn } from '$lib/utils';

type Option = {
	value: string;
	label: string;
};

type Props = {
	value?: string;
	options: Option[];
	placeholder?: string;
	disabled?: boolean;
	class?: string;
	id?: string;
	name?: string;
	onValueChange?: (value: string) => void;
};

let {
	value = $bindable(''),
	options,
	placeholder = 'Select an option',
	disabled = false,
	class: className = '',
	id,
	name,
	onValueChange,
	...restProps
}: Props = $props();

function handleChange(event: Event) {
	const target = event.target as HTMLSelectElement;
	value = target.value;
	onValueChange?.(value);
}
</script>

<select
bind:value
{disabled}
{id}
{name}
class={cn(
	'flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 transition-all duration-200 hover:border-accent/50 focus:border-accent',
	className
)}
onchange={handleChange}
{...restProps}
>
{#if placeholder}
	<option value="" disabled>{placeholder}</option>
{/if}
{#each options as option}
	<option value={option.value}>{option.label}</option>
{/each}
</select>
