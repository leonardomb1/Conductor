<script lang="ts">
  import { auth } from "$lib/auth.svelte.js"
  import { themeStore } from "$lib/stores/theme.svelte.js"
  import { api } from "$lib/api.js"
  import { goto } from "$app/navigation"
  import { User, LogOut, Menu, Sun, Moon } from "@lucide/svelte"
  import { onMount } from "svelte"

  let showUserMenu = $state(false)
  let healthData = $state<any>(null)

  onMount(async () => {
    if (auth.isAuthenticated) {
      try {
        healthData = await api.getHealth()
      } catch (error) {
        console.error('Failed to load health data:', error)
      }
    }
  })

  function handleLogout() {
    auth.logout()
    goto("/login")
  }

  function toggleUserMenu() {
    showUserMenu = !showUserMenu
  }

  function handleClickOutside(event: MouseEvent) {
    if (
      showUserMenu &&
      !(event.target as Element)?.closest(".user-menu-container")
    ) {
      showUserMenu = false
    }
  }

  function toggleMobileMenu() {
    themeStore.toggleMobileMenu()
  }

  onMount(() => {
    document.addEventListener("click", handleClickOutside)
    return () => document.removeEventListener("click", handleClickOutside)
  })
</script>

<header class="bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 shadow-sm">
  <div class="flex items-center justify-between h-16 px-4 sm:px-6">
    <!-- Mobile menu button -->
    <div class="flex items-center lg:hidden">
      <button
        onclick={toggleMobileMenu}
        class="p-2 rounded-md text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-supabase-green"
      >
        <Menu size={20} />
      </button>
    </div>

    <!-- Desktop logo (hidden on mobile) -->
    <div class="hidden lg:flex lg:items-center">
      <!-- Empty div to maintain layout balance -->
    </div>

    <!-- Mobile logo (centered) -->
    <div class="flex items-center lg:hidden">
      <div class="w-8 h-8 bg-supabase-green rounded-lg flex items-center justify-center">
        <span class="text-white font-bold text-lg">C</span>
      </div>
      <span class="ml-3 text-xl font-semibold text-gray-900 dark:text-white">Conductor</span>
    </div>

    <!-- Right side - System status and controls -->
    <div class="flex items-center space-x-2 sm:space-x-4">
      <!-- System status (hidden on small screens) -->
      {#if healthData}
        <div class="hidden sm:flex items-center space-x-4 text-sm text-gray-600 dark:text-gray-400">
          <!-- System Health Status -->
          <div class="flex items-center space-x-2">
            <div class="w-2 h-2 bg-green-500 rounded-full"></div>
            <span class="hidden md:inline">System Healthy</span>
            <span class="md:hidden">Online</span>
          </div>
        </div>
      {:else if auth.isAuthenticated}
        <div class="hidden sm:flex items-center space-x-2 text-sm text-gray-600 dark:text-gray-400">
          <div class="w-2 h-2 bg-blue-500 rounded-full"></div>
          <span class="hidden md:inline">System Online</span>
          <span class="md:hidden">Online</span>
        </div>
      {/if}

      <!-- Theme toggle -->
      <button
        onclick={() => themeStore.toggleTheme()}
        class="p-2 rounded-md text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-supabase-green transition-colors"
        title={themeStore.isDark ? 'Switch to light mode' : 'Switch to dark mode'}
      >
        {#if themeStore.isDark}
          <Sun size={18} />
        {:else}
          <Moon size={18} />
        {/if}
      </button>

      <!-- User Menu -->
      <div class="relative user-menu-container">
        <button
          onclick={toggleUserMenu}
          class="flex items-center space-x-2 p-2 text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-white transition-colors rounded-md hover:bg-gray-100 dark:hover:bg-gray-700"
        >
          <User size={18} />
          <span class="hidden sm:inline text-sm font-medium">{auth.user || "User"}</span>
        </button>

        {#if showUserMenu}
          <div
            class="absolute right-0 mt-2 w-48 bg-white dark:bg-gray-800 rounded-md shadow-lg ring-1 ring-black ring-opacity-5 dark:ring-gray-700 z-50"
          >
            <div class="py-1">
              <div
                class="px-4 py-2 text-sm text-gray-500 dark:text-gray-400 border-b border-gray-100 dark:border-gray-700"
              >
                Signed in as <br />
                <span class="font-medium text-gray-900 dark:text-white"
                  >{auth.user}</span
                >
              </div>
              
              <!-- Theme toggle in menu (mobile) -->
              <button
                onclick={() => {themeStore.toggleTheme(); showUserMenu = false}}
                class="flex items-center w-full px-4 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 sm:hidden"
              >
                {#if themeStore.isDark}
                  <Sun size={16} class="mr-2" />
                  Light mode
                {:else}
                  <Moon size={16} class="mr-2" />
                  Dark mode
                {/if}
              </button>
              
              <button
                onclick={handleLogout}
                class="flex items-center w-full px-4 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700"
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