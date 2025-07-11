<script lang="ts">
  import '../app.css';
  import { page } from '$app/stores';
  import { auth } from '$lib/auth.svelte.js';
  import { goto } from '$app/navigation';
  import { onMount } from 'svelte';
  import Sidebar from '$lib/components/layout/Sidebar.svelte';
  import Header from '$lib/components/layout/Header.svelte';

  let { children } = $props();

  $effect(() => {
    // Redirect to login if not authenticated and not on login page
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
{/if}
