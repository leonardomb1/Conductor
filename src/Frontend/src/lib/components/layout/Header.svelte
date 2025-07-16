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
        console.error("Failed to load health data:", error)
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

<header
  class="bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 shadow-sm"
>
  <div class="flex items-center justify-between h-16 px-4 sm:px-6">
    <!-- Mobile menu button -->
    <div class="flex items-center lg:hidden">
      <button
        onclick={toggleMobileMenu}
        class="mobile-header-button"
        aria-label="Toggle mobile menu"
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
      <div
        class="w-8 h-8 bg-supabase-green rounded-lg flex items-center justify-center"
      >
        <span class="text-white font-bold text-lg">C</span>
      </div>
      <span class="ml-3 text-xl font-semibold text-gray-900 dark:text-white"
        >Conductor</span
      >
    </div>

    <!-- Right side - System status and controls -->
    <div class="flex items-center space-x-2 sm:space-x-4">
      <!-- System status (hidden on small screens) -->
      {#if healthData}
        <div
          class="hidden sm:flex items-center space-x-4 text-sm text-gray-600 dark:text-gray-400"
        >
          <!-- System Health Status -->
          <div class="flex items-center space-x-2">
            <div class="w-2 h-2 bg-green-500 rounded-full"></div>
            <span class="hidden md:inline">System Healthy</span>
            <span class="md:hidden">Online</span>
          </div>
        </div>
      {:else if auth.isAuthenticated}
        <div
          class="hidden sm:flex items-center space-x-2 text-sm text-gray-600 dark:text-gray-400"
        >
          <div class="w-2 h-2 bg-blue-500 rounded-full"></div>
          <span class="hidden md:inline">System Online</span>
          <span class="md:hidden">Online</span>
        </div>
      {/if}

      <!-- Theme toggle -->
      <button
        onclick={() => themeStore.toggleTheme()}
        class="mobile-header-button"
        title={themeStore.isDark
          ? "Switch to light mode"
          : "Switch to dark mode"}
        aria-label={themeStore.isDark
          ? "Switch to light mode"
          : "Switch to dark mode"}
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
          class="desktop-user-button sm:mobile-header-button"
          aria-label="User menu"
        >
          <User size={18} />
          <span class="hidden sm:inline text-sm font-medium ml-2"
            >{auth.user || "User"}</span
          >
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
                onclick={() => {
                  themeStore.toggleTheme()
                  showUserMenu = false
                }}
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

<style>
  /* Mobile header button styling with perfect icon centering */
  .mobile-header-button {
    /* Ensure flexbox centering */
    display: inline-flex;
    align-items: center;
    justify-content: center;

    /* Minimum touch target size for mobile */
    min-height: 44px;
    min-width: 44px;

    /* Padding and spacing */
    padding: 0.75rem; /* 12px */

    /* Visual styling */
    border-radius: 0.5rem; /* 8px */
    color: rgb(156 163 175); /* text-gray-400 */
    background-color: transparent;
    border: none;
    cursor: pointer;

    /* Transition effects */
    transition: all 0.15s ease-in-out;

    /* Remove any inherited text alignment */
    text-align: center;
    line-height: 1;

    /* Prevent button from shrinking */
    flex-shrink: 0;
  }

  .mobile-header-button:hover {
    color: rgb(75 85 99); /* text-gray-600 */
    background-color: rgb(243 244 246); /* bg-gray-100 */
  }

  :global(.dark) .mobile-header-button:hover {
    color: rgb(209 213 219); /* dark:text-gray-300 */
    background-color: rgb(55 65 81); /* dark:bg-gray-700 */
  }

  .mobile-header-button:focus {
    outline: none;
    box-shadow: 0 0 0 2px rgb(34 197 94); /* focus:ring-2 focus:ring-supabase-green */
  }

  /* SVG icon styling within mobile buttons */
  .mobile-header-button :global(svg) {
    /* Ensure SVG is perfectly centered */
    display: block;
    margin: 0;

    /* Prevent any positioning issues */
    position: relative;
    top: 0;
    left: 0;
    transform: none;

    /* Prevent SVG from inheriting text styles */
    vertical-align: baseline;

    /* Ensure consistent sizing */
    flex-shrink: 0;

    /* Hardware acceleration for smooth interactions */
    transform: translateZ(0);
    backface-visibility: hidden;
  }

  /* Desktop user button (with text) */
  .desktop-user-button {
    display: flex;
    align-items: center;
    padding: 0.5rem;
    color: rgb(55 65 81); /* text-gray-700 */
    border-radius: 0.375rem; /* 6px */
    transition: all 0.15s ease-in-out;
    background-color: transparent;
    border: none;
    cursor: pointer;
    min-height: 44px; /* Ensure touch target on mobile */
  }

  .desktop-user-button:hover {
    color: rgb(17 24 39); /* text-gray-900 */
    background-color: rgb(243 244 246); /* bg-gray-100 */
  }

  :global(.dark) .desktop-user-button {
    color: rgb(209 213 219); /* dark:text-gray-300 */
  }

  :global(.dark) .desktop-user-button:hover {
    color: rgb(255 255 255); /* dark:text-white */
    background-color: rgb(55 65 81); /* dark:bg-gray-700 */
  }

  /* Mobile-specific adjustments */
  @media (max-width: 640px) {
    .mobile-header-button {
      /* Ensure proper spacing on mobile */
      margin: 0 0.125rem; /* 2px horizontal margin */
    }

    /* Hide text on mobile for user button */
    .desktop-user-button span {
      display: none;
    }

    /* Make user button consistent with other mobile buttons */
    .desktop-user-button {
      min-height: 44px;
      min-width: 44px;
      justify-content: center;
      padding: 0.75rem;
    }
  }

  /* Ensure proper touch behavior on iOS */
  @supports (-webkit-touch-callout: none) {
    .mobile-header-button {
      -webkit-appearance: none;
      appearance: none;
      -webkit-tap-highlight-color: transparent;
    }

    .mobile-header-button :global(svg) {
      pointer-events: none; /* Prevent touch issues on iOS */
    }
  }

  /* Fix for any layout shift issues */
  .mobile-header-button:active {
    transform: scale(0.98);
  }

  /* Ensure consistent button alignment */
  @media (max-width: 640px) {
    .mobile-header-button,
    .desktop-user-button {
      /* Ensure all buttons have the same dimensions */
      width: 44px;
      height: 44px;
      padding: 0;

      /* Perfect centering */
      display: inline-flex !important;
      align-items: center !important;
      justify-content: center !important;
    }
  }
</style>
