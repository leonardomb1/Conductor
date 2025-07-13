<script lang="ts">
  import { page } from "$app/stores"
  import {
    Database,
    FileText,
    Users,
    ChartBar,
    Calendar,
    Download,
    Upload,
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
</script>

<div class="flex flex-col w-64 bg-white border-r border-supabase-gray-200">
  <div class="flex items-center h-16 px-6 border-b border-supabase-gray-200">
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

  <nav class="flex-1 px-4 py-6 space-y-1">
    {#each navigation as item}
      {@const active = isActive(item.href)}
      {@const IconComponent = item.icon}
      <a
        href={item.href}
        class="flex items-center px-3 py-2 text-sm font-medium rounded-md transition-colors"
        class:bg-supabase-green={active}
        class:text-white={active}
        class:text-supabase-gray-700={!active}
        class:hover:bg-supabase-gray-100={!active}
        data-sveltekit-preload-data="hover"
      >
        <IconComponent size={20} class="mr-3" />
        {item.name}
      </a>
    {/each}
  </nav>
</div>
