<script lang="ts">
  interface Props {
    variant?: 'default' | 'success' | 'warning' | 'error' | 'info';
    size?: 'sm' | 'md';
    children: any;
    class?: string;
  }

  let {
    variant = 'default',
    size = 'md',
    children,
    class: className = ''
  }: Props = $props();

  const variants = {
    default: 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-200',
    success: 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300',
    warning: 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-300',
    error: 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300',
    info: 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300'
  };

  const sizes = {
    sm: 'px-2 py-1 text-xs',
    md: 'px-2.5 py-0.5 text-sm'
  };

  const classes = `inline-flex items-center rounded-full font-medium ${variants[variant]} ${sizes[size]} ${className}`;
</script>

<span class={classes}>
  {@render children()}
</span>