<script lang="ts">
	import '../app.css';
	import ToastContainer from '$lib/components/ToastContainer.svelte';
	interface Props {
		children?: import('svelte').Snippet;
	}

	let { children }: Props = $props();
</script>

<main>
	{@render children?.()}
</main>

<ToastContainer />
