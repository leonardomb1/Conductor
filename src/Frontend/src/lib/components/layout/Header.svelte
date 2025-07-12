<script lang="ts">
  import { auth } from "$lib/auth.svelte.js"
  import { api } from "$lib/api.js"
  import { goto } from "$app/navigation"
  import { Bell, User, LogOut } from "@lucide/svelte"
  import { onMount } from "svelte"

  let showUserMenu = $state(false)
  let healthData = $state<any>(null)

  // Simple health check on mount - no intervals, no loops
  onMount(async () => {
    if (auth.isAuthenticated) {
      try {
        healthData = await api.getHealth()
      } catch (error) {
        // Silently fail - health check is optional
        console.debug("Health check failed:", error)
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

  // Close user menu when clicking outside
  function handleClickOutside(event: MouseEvent) {
    if (
      showUserMenu &&
      !(event.target as Element)?.closest(".user-menu-container")
    ) {
      showUserMenu = false
    }
  }

  onMount(() => {
    document.addEventListener("click", handleClickOutside)
    return () => document.removeEventListener("click", handleClickOutside)
  })
</script>

<header class="bg-white border-b border-supabase-gray-200">
  <div class="flex items-center justify-between h-16 px-6">
    <!-- Left side - Brand/Logo -->
    <div class="flex items-center">
      <div class="flex items-center">
        <div
          class="w-8 h-8 bg-supabase-green rounded-lg flex items-center justify-center"
        >
          <span class="text-white font-bold text-lg">C</span>
        </div>
        <span class="ml-3 text-xl font-semibold text-supabase-gray-900"
          >Conductor</span
        >
      </div>
    </div>

    <!-- Right side - System status and user menu -->
    <div class="flex items-center space-x-6">
      <!-- System status -->
      {#if healthData}
        <div class="flex items-center space-x-2 text-sm text-supabase-gray-600">
          <div class="w-2 h-2 bg-green-500 rounded-full"></div>
          <span>System Healthy</span>
          <span class="text-supabase-gray-400">•</span>
          <span>{healthData.activeJobs} active jobs</span>
        </div>
      {:else if auth.isAuthenticated}
        <div class="flex items-center space-x-2 text-sm text-supabase-gray-600">
          <div class="w-2 h-2 bg-blue-500 rounded-full"></div>
          <span>System Online</span>
        </div>
      {/if}

      <!-- Notifications -->
      <button
        class="p-2 text-supabase-gray-400 hover:text-supabase-gray-600 transition-colors"
      >
        <Bell size={20} />
      </button>

      <!-- User Menu -->
      <div class="relative user-menu-container">
        <button
          onclick={toggleUserMenu}
          class="flex items-center space-x-2 p-2 text-supabase-gray-700 hover:text-supabase-gray-900 transition-colors"
        >
          <User size={20} />
          <span class="text-sm font-medium">{auth.user || "User"}</span>
        </button>

        {#if showUserMenu}
          <div
            class="absolute right-0 mt-2 w-48 bg-white rounded-md shadow-lg ring-1 ring-black ring-opacity-5 z-50"
          >
            <div class="py-1">
              <div
                class="px-4 py-2 text-sm text-supabase-gray-500 border-b border-supabase-gray-100"
              >
                Signed in as <br />
                <span class="font-medium text-supabase-gray-900"
                  >{auth.user}</span
                >
              </div>
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
