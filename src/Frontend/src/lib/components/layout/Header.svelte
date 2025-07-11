<script lang="ts">
  import { auth } from '$lib/auth.svelte.js';
  import { goto } from '$app/navigation';
  import { Bell, Search, User, LogOut } from '@lucide/svelte';
  import { onMount } from 'svelte';
  
  let showUserMenu = $state(false);
  let healthData = $state<any>(null);

  onMount(async () => {
    try {
      const response = await fetch('/api/health');
      healthData = await response.json();
    } catch (error) {
      console.error('Failed to fetch health data:', error);
    }
  });

  function handleLogout() {
    auth.logout();
    goto('/login');
  }

  function toggleUserMenu() {
    showUserMenu = !showUserMenu;
  }
</script>

<header class="bg-white border-b border-supabase-gray-200">
  <div class="flex items-center justify-between h-16 px-6">
    <div class="flex items-center flex-1">
      <div class="max-w-lg w-full lg:max-w-xs">
        <div class="relative">
          <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
            <Search class="h-5 w-5 text-supabase-gray-400" />
          </div>
          <input
            class="block w-full pl-10 pr-3 py-2 border border-supabase-gray-300 rounded-md leading-5 bg-white placeholder-supabase-gray-500 focus:outline-none focus:placeholder-supabase-gray-400 focus:ring-1 focus:ring-supabase-green focus:border-supabase-green sm:text-sm"
            placeholder="Search..."
            type="search"
          />
        </div>
      </div>
    </div>

    <div class="flex items-center space-x-4">
      {#if healthData}
        <div class="flex items-center space-x-2 text-sm text-supabase-gray-600">
          <div class="w-2 h-2 bg-green-500 rounded-full"></div>
          <span>System Healthy</span>
          <span class="text-supabase-gray-400">•</span>
          <span>{healthData.activeJobs} active jobs</span>
        </div>
      {/if}

      <button class="p-2 text-supabase-gray-400 hover:text-supabase-gray-600 transition-colors">
        <Bell size={20} />
      </button>

      <div class="relative">
        <button
          onclick={toggleUserMenu}
          class="flex items-center space-x-2 p-2 text-supabase-gray-700 hover:text-supabase-gray-900 transition-colors"
        >
          <User size={20} />
          <span class="text-sm font-medium">{auth.user}</span>
        </button>

        {#if showUserMenu}
          <!-- svelte-ignore a11y_click_events_have_key_events -->
          <!-- svelte-ignore a11y_no_static_element_interactions -->
          <div 
            class="absolute right-0 mt-2 w-48 bg-white rounded-md shadow-lg ring-1 ring-black ring-opacity-5 z-50"
            onclick={() => showUserMenu = false}
          >
            <div class="py-1">
              <button
                onclick={handleLogout}
                class="flex items-center w-full px-4 py-2 text-sm text-supabase-gray-700 hover:bg-supabase-gray-100"
              >
                <LogOut size={16} class="mr-2" />
                Sign out
              </button>
            </div>
          </div>
        {/if}
      </div>
    </div>
  </div>
</header>
