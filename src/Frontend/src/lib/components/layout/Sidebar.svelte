<script lang="ts">
  import { page } from "$app/stores"
  import { themeStore } from "$lib/stores/theme.svelte.js"
  import {
    Database,
    FileText,
    Users,
    ChartBar,
    Calendar,
    Download,
    Upload,
    X,
  } from "@lucide/svelte"

  const navigation = [
    { name: "Dashboard", href: "/dashboard", icon: ChartBar },
    { name: "Extractions", href: "/extractions", icon: Download },
    { name: "Origins", href: "/origins", icon: Database },
    { name: "Destinations", href: "/destinations", icon: Upload },
    { name: "Schedules", href: "/schedules", icon: Calendar },
    { name: "Jobs", href: "/jobs", icon: FileText },
    { name: "Users", href: "/users", icon: Users },
  ]

  // Fixed isActive function to handle exact matches and nested routes properly
  function isActive(href: string) {
    const pathname = $page.url.pathname

    // Handle root dashboard case
    if (href === "/dashboard" && pathname === "/") {
      return true
    }

    // For other routes, check if pathname starts with href
    // but ensure it's not a partial match (e.g., /extract shouldn't match /extractions)
    if (pathname === href) {
      return true // Exact match
    }

    // Check if it's a nested route (pathname starts with href followed by /)
    if (pathname.startsWith(href + "/")) {
      return true
    }

    return false
  }

  function handleNavClick() {
    themeStore.closeMobileMenu()
  }

  function handleOverlayClick() {
    themeStore.closeMobileMenu()
  }
</script>

<!-- Mobile menu overlay -->
{#if themeStore.isMobileMenuOpen}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div 
    class="mobile-menu-overlay"
    onclick={handleOverlayClick}
  ></div>
{/if}

<!-- Desktop sidebar -->
<div class="hidden lg:flex lg:flex-col lg:w-64 lg:bg-white lg:dark:bg-gray-800 lg:border-r lg:border-gray-200 lg:dark:border-gray-700">
  <div class="flex items-center h-16 px-6 border-b border-gray-200 dark:border-gray-700">
    <div class="flex items-center">
      <div
        class="w-8 h-8 bg-supabase-green rounded-lg flex items-center justify-center"
      >
        <span class="text-white font-bold text-lg">C</span>
      </div>
      <span class="ml-3 text-xl font-semibold text-gray-900 dark:text-white"
        >Conductor</span
      >
    </div>
  </div>

  <nav class="flex-1 px-4 py-6 space-y-1 overflow-y-auto">
    {#each navigation as item}
      {@const active = isActive(item.href)}
      {@const IconComponent = item.icon}
      <a
        href={item.href}
        class="flex items-center px-3 py-2 text-sm font-medium rounded-md transition-colors"
        class:bg-supabase-green={active}
        class:text-white={active}
        class:text-gray-700={!active}
        class:dark:text-gray-300={!active}
        class:hover:bg-gray-100={!active}
        class:dark:hover:bg-gray-700={!active}
        data-sveltekit-preload-data="hover"
      >
        <IconComponent size={20} class="mr-3" />
        {item.name}
      </a>
    {/each}
  </nav>
</div>

<!-- Mobile sidebar -->
<div 
  class="mobile-menu"
  class:open={themeStore.isMobileMenuOpen}
  class:closed={!themeStore.isMobileMenuOpen}
>
  <div class="flex items-center justify-between h-16 px-6 border-b border-gray-200 dark:border-gray-700">
    <div class="flex items-center">
      <div
        class="w-8 h-8 bg-supabase-green rounded-lg flex items-center justify-center"
      >
        <span class="text-white font-bold text-lg">C</span>
      </div>
      <span class="ml-3 text-xl font-semibold text-gray-900 dark:text-white"
        >Conductor</span
      >
    </div>
    <button
      onclick={() => themeStore.closeMobileMenu()}
      class="p-2 rounded-md text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700"
    >
      <X size={20} />
    </button>
  </div>

  <nav class="flex-1 px-4 py-6 space-y-1 overflow-y-auto">
    {#each navigation as item}
      {@const active = isActive(item.href)}
      {@const IconComponent = item.icon}
      <a
        href={item.href}
        onclick={handleNavClick}
        class="flex items-center px-3 py-2 text-sm font-medium rounded-md transition-colors"
        class:bg-supabase-green={active}
        class:text-white={active}
        class:text-gray-700={!active}
        class:dark:text-gray-300={!active}
        class:hover:bg-gray-100={!active}
        class:dark:hover:bg-gray-700={!active}
        data-sveltekit-preload-data="hover"
      >
        <IconComponent size={20} class="mr-3" />
        {item.name}
      </a>
    {/each}
  </nav>
</div>