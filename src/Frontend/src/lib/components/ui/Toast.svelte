<script lang="ts">
import { cn } from '$lib/utils';
import { tv, type VariantProps } from 'tailwind-variants';

const toastVariants = tv({
	base: 'group pointer-events-auto relative flex w-full items-center justify-between space-x-4 overflow-hidden rounded-md border p-6 pr-8 shadow-lg transition-all',
	variants: {
		variant: {
			default: 'border bg-background text-foreground shadow-metallic',
			destructive: 'destructive border-destructive bg-destructive text-destructive-foreground',
			success: 'border-metallic-blue-200 bg-metallic-blue-50 text-metallic-blue-800 dark:border-metallic-blue-800 dark:bg-metallic-blue-950/50 dark:text-metallic-blue-200'
		}
	},
	defaultVariants: {
		variant: 'default'
	}
});

type Props = {
	variant?: VariantProps<typeof toastVariants>['variant'];
	class?: string;
	children?: any;
};

let { variant = 'default', class: className, children, ...restProps }: Props = $props();
</script>

<div class={cn(toastVariants({ variant }), className)} {...restProps}>
{@render children?.()}
</div>
