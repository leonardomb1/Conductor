<script lang="ts">
  import '../app.css';
  import { page } from '$app/stores';
  import { auth } from '$lib/auth.svelte.js';
  import { goto } from '$app/navigation';
  import Sidebar from '$lib/components/layout/Sidebar.svelte';
  import Header from '$lib/components/layout/Header.svelte';

  let { children } = $props();

  // Simple reactive navigation
  $effect(() => {
    const isLoginPage = $page.url.pathname.startsWith('/login');
    
    if (!auth.isAuthenticated && !isLoginPage) {
      goto('/login');
    } else if (auth.isAuthenticated && isLoginPage) {
      goto('/dashboard');
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
  <!-- Loading while redirecting -->
  <div class="flex min-h-screen items-center justify-center bg-supabase-gray-50">
    <div class="text-center">
      <div class="animate-spin h-8 w-8 border-4 border-supabase-green border-t-transparent rounded-full mx-auto"></div>
      <p class="mt-2 text-supabase-gray-600">Loading...</p>
    </div>
  </div>
{/if}