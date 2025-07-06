<script lang="ts">
import { createEventDispatcher } from 'svelte';

type Props = {
	open?: boolean;
	children?: any;
	close?: () => void;
};

let { open = false, children, close }: Props = $props();

function handleClose() {
	open = false;
	if (close) {
		close();
	}
}

function handleBackdropClick(event: MouseEvent) {
	if (event.target === event.currentTarget) {
		handleClose();
	}
}
</script>

{#if open}
<div class="fixed inset-0 z-50 flex items-center justify-center" role="dialog" aria-modal="true" tabindex="-1" onkeydown={(event) => { if (event.key === 'Escape') handleClose(); }}>
	<div class="fixed inset-0 bg-background/80 backdrop-blur-sm" role="button" tabindex="0" onclick={handleBackdropClick} onkeydown={(event) => { if (event.key === 'Enter') handleClose(); }}></div>
	<div class="relative z-50 grid w-full max-w-lg gap-4 border bg-background p-6 shadow-lg duration-200 sm:rounded-lg metallic-card shadow-metallic-xl">
		{@render children?.()}
	</div>
</div>
{/if}
