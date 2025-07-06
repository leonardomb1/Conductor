<script lang="ts">
import { cn } from '$lib/utils';
import { tv, type VariantProps } from 'tailwind-variants';

const buttonVariants = tv({
	base: 'inline-flex items-center justify-center whitespace-nowrap rounded-md text-sm font-medium ring-offset-background transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50',
	variants: {
		variant: {
			default: 'bg-primary text-primary-foreground hover:bg-primary/90 shadow-metallic hover:shadow-metallic-lg hover:-translate-y-0.5 active:translate-y-0',
			destructive: 'bg-destructive text-destructive-foreground hover:bg-destructive/90 shadow-sm hover:shadow-md',
			outline: 'border border-input bg-background hover:bg-accent hover:text-accent-foreground shadow-sm hover:shadow-metallic',
			secondary: 'bg-secondary text-secondary-foreground hover:bg-secondary/80 shadow-sm hover:shadow-metallic',
			ghost: 'hover:bg-accent hover:text-accent-foreground',
			link: 'text-primary underline-offset-4 hover:underline',
			metallic: 'btn-metallic metallic-shine'
		},
		size: {
			default: 'h-10 px-4 py-2',
			sm: 'h-9 rounded-md px-3',
			lg: 'h-11 rounded-md px-8',
			icon: 'h-10 w-10'
		}
	},
	defaultVariants: {
		variant: 'default',
		size: 'default'
	}
});

type Props = {
	variant?: VariantProps<typeof buttonVariants>['variant'];
	size?: VariantProps<typeof buttonVariants>['size'];
	disabled?: boolean;
	type?: 'button' | 'submit' | 'reset';
	onclick?: () => void;
	class?: string;
	children?: any;
};

let { variant = 'default', size = 'default', disabled = false, type = 'button', onclick, class: className, children, ...restProps }: Props = $props();
</script>

<button
{type}
{disabled}
onclick={onclick}
class={cn(buttonVariants({ variant, size }), className)}
{...restProps}
>
{@render children?.()}
</button>
