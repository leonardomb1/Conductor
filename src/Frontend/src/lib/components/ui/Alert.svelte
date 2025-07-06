<script lang="ts">
import { cn } from '$lib/utils';
import { tv, type VariantProps } from 'tailwind-variants';

const alertVariants = tv({
	base: 'relative w-full rounded-lg border p-4 [&>svg~*]:pl-7 [&>svg+div]:translate-y-[-3px] [&>svg]:absolute [&>svg]:left-4 [&>svg]:top-4 [&>svg]:text-foreground shadow-sm',
	variants: {
		variant: {
			default: 'bg-background text-foreground border-border',
			destructive: 'border-destructive/50 text-destructive dark:border-destructive [&>svg]:text-destructive bg-destructive/5',
			info: 'border-metallic-blue-200 text-metallic-blue-800 dark:border-metallic-blue-800 dark:text-metallic-blue-200 bg-metallic-blue-50 dark:bg-metallic-blue-950/50 [&>svg]:text-metallic-blue-600 dark:[&>svg]:text-metallic-blue-400'
		}
	},
	defaultVariants: {
		variant: 'default'
	}
});

type Props = {
	variant?: VariantProps<typeof alertVariants>['variant'];
	class?: string;
	children?: any;
};

let { variant = 'default', class: className, children, ...restProps }: Props = $props();
</script>

<div class={cn(alertVariants({ variant }), className)} {...restProps}>
{@render children?.()}
</div>
