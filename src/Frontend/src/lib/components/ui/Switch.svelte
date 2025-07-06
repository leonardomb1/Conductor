<script lang="ts">
import { cn } from '$lib/utils';

type Props = {
	checked?: boolean;
	disabled?: boolean;
	class?: string;
	id?: string;
	name?: string;
};

let {
	checked = $bindable(false),
	disabled = false,
	class: className = '',
	id,
	name,
	...restProps
}: Props = $props();

function toggleChecked() {
	if (!disabled) {
		checked = !checked;
	}
}
</script>

<button
type="button"
role="switch"
aria-checked={checked}
{disabled}
{id}
{name}
class={cn(
	'peer inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background disabled:cursor-not-allowed disabled:opacity-50',
	checked ? 'bg-primary shadow-metallic' : 'bg-input hover:bg-muted',
	className
)}
data-state={checked ? 'checked' : 'unchecked'}
onclick={toggleChecked}
{...restProps}
>
<span
	class={cn(
		'pointer-events-none block h-5 w-5 rounded-full bg-background shadow-lg ring-0 transition-transform duration-200',
		checked ? 'translate-x-5' : 'translate-x-0'
	)}
	data-state={checked ? 'checked' : 'unchecked'}
></span>
</button>
