<script lang="ts">
  interface Props {
    variant?: 'default' | 'success' | 'warning' | 'error' | 'info';
    size?: 'sm' | 'md';
    children: any;
  }

  let {
    variant = 'default',
    size = 'md',
    children
  }: Props = $props();

  const variants = {
    default: 'bg-supabase-gray-100 text-supabase-gray-800',
    success: 'bg-green-100 text-green-800',
    warning: 'bg-yellow-100 text-yellow-800',
    error: 'bg-red-100 text-red-800',
    info: 'bg-blue-100 text-blue-800'
  };

  const sizes = {
    sm: 'px-2 py-1 text-xs',
    md: 'px-2.5 py-0.5 text-sm'
  };

  const classes = `inline-flex items-center rounded-full font-medium ${variants[variant]} ${sizes[size]}`;
</script>

<span class={classes}>
  {@render children()}
</span>
