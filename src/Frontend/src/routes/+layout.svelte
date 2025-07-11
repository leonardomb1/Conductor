<script lang="ts">
  import '../app.css';
  import { page } from '$app/stores';
  import { auth } from '$lib/auth.svelte.js';
  import { goto } from '$app/navigation';
  import { onMount } from 'svelte';
  import Sidebar from '$lib/components/layout/Sidebar.svelte';
  import Header from '$lib/components/layout/Header.svelte';

  let { children } = $props();
  let isInitialized = $state(false);

  // Initialize auth state on mount
  onMount(() => {
    isInitialized = true;
  });

  // Navigation logic with better performance
  $effect(() => {
    if (!isInitialized) return;
    
    const isLoginPage = $page.url.pathname.startsWith('/login');
    
    // Only redirect if we have a clear auth state
    if (!auth.isAuthenticated && !isLoginPage) {
      goto('/login', { replaceState: true });
    } else if (auth.isAuthenticated && isLoginPage) {
      goto('/dashboard', { replaceState: true });
    }
  });

  const isLoginPage = $derived($page.url.pathname.startsWith('/login'));
  const shouldShowApp = $derived(isInitialized && !isLoginPage && auth.isAuthenticated);
  const shouldShowLogin = $derived(isInitialized && isLoginPage);
  const shouldShowLoading = $derived(!isInitialized || (!isLoginPage && !auth.isAuthenticated));
</script>

<!-- Optimize head tags -->
<svelte:head>
  <meta name="description" content="Conductor - ETL Data Pipeline Management System" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <link rel="preconnect" href="/api" />
</svelte:head>

{#if shouldShowLogin}
  {@render children()}
{:else if shouldShowApp}
  <div class="flex h-screen bg-supabase-gray-50">
    <Sidebar />
    <div class="flex-1 flex flex-col overflow-hidden">
      <Header />
      <main class="flex-1 overflow-y-auto">
        <div class="p-6">
          {@render children()}
        </div>
      </main>
    </div>
  </div>
{:else if shouldShowLoading}
  <!-- Optimized loading screen -->
  <div class="flex min-h-screen items-center justify-center bg-supabase-gray-50">
    <div class="text-center">
      <div class="inline-flex items-center justify-center w-16 h-16 bg-supabase-green rounded-full mb-4">
        <span class="text-white font-bold text-2xl">C</span>
      </div>
      <div class="animate-spin h-8 w-8 border-4 border-supabase-green border-t-transparent rounded-full mx-auto mb-4"></div>
      <p class="text-supabase-gray-600 text-sm">Loading Conductor...</p>
    </div>
  </div>
{/if}

<style>
  /* Optimize CSS for better performance */
  :global(body) {
    font-feature-settings: 'kern' 1;
    text-rendering: optimizeLegibility;
    -webkit-font-smoothing: antialiased;
    -moz-osx-font-smoothing: grayscale;
  }

  /* Improve table performance */
  :global(.table-container) {
    contain: layout style;
  }

  /* Optimize modal transitions */
  :global(.modal-backdrop) {
    will-change: opacity;
  }

  /* Reduce paint operations */
  :global(.hover\\:bg-) {
    transition: background-color 0.15s ease-in-out;
  }
</style>