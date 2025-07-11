<script lang="ts">
  import '../app.css';
  import { page } from '$app/stores';
  import { auth } from '$lib/auth.svelte.js';
  import { goto } from '$app/navigation';
  import { onMount } from 'svelte';
  import Sidebar from '$lib/components/layout/Sidebar.svelte';
  import Header from '$lib/components/layout/Header.svelte';

  let { children } = $props();

  onMount(async () => {
    // Validate token on app startup
    if (auth.isAuthenticated && !await auth.validateToken()) {
      // Token was invalid, user has been logged out
      goto('/login');
    }
  });

  $effect(() => {
    if (!auth.isAuthenticated && !$page.url.pathname.startsWith('/login')) {
      goto('/login');
    }
  });

  const isLoginPage = $derived($page.url.pathname.startsWith('/login'));
</script>

{#if isLoginPage}
  {@render children()}
{:else if auth.isAuthenticated}
  <div class="flex h-screen bg-supabase-gray-50">
    <Sidebar />
    <div class="flex-1 flex flex-col overflow-hidden">
      <Header />
      <main class="flex-1 overflow-y-auto p-6">
        {@render children()}
      </main>
    </div>
  </div>
{:else}
  <!-- Loading state while checking authentication -->
  <div class="flex min-h-screen items-center justify-center bg-supabase-gray-50">
    <div class="text-center">
      <div class="animate-spin h-8 w-8 border-4 border-supabase-green border-t-transparent rounded-full mx-auto"></div>
      <p class="mt-2 text-supabase-gray-600">Loading...</p>
    </div>
  </div>
{/if}