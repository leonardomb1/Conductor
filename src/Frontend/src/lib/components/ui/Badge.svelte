<script lang="ts">
import { cn } from '$lib/utils';
import { tv, type VariantProps } from 'tailwind-variants';

const badgeVariants = tv({
	base: 'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold transition-colors focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2',
	variants: {
		variant: {
			default: 'border-transparent bg-primary text-primary-foreground hover:bg-primary/80 shadow-metallic',
			secondary: 'border-transparent bg-secondary text-secondary-foreground hover:bg-secondary/80',
			destructive: 'border-transparent bg-destructive text-destructive-foreground hover:bg-destructive/80',
			outline: 'text-foreground border-border hover:bg-accent hover:text-accent-foreground',
			metallic: 'border-transparent bg-metallic-blue-500 text-white shadow-metallic hover:bg-metallic-blue-600'
		}
	},
	defaultVariants: {
		variant: 'default'
	}
});

type Props = {
	variant?: VariantProps<typeof badgeVariants>['variant'];
	class?: string;
	children?: any;
};

let { variant = 'default', class: className, children, ...restProps }: Props = $props();
</script>

<div class={cn(badgeVariants({ variant }), className)} {...restProps}>
{@render children?.()}
</div>
