<script lang="ts">
  import { page } from '$app/stores';
  import { 
    Database, 
    FileText, 
    Users, 
    Settings, 
    BarChart3,
    Calendar,
    Download,
    Upload
  } from 'lucide-svelte';

  const navigation = [
    { name: 'Dashboard', href: '/dashboard', icon: BarChart3 },
    { name: 'Extractions', href: '/extractions', icon: Download },
    { name: 'Origins', href: '/origins', icon: Database },
    { name: 'Destinations', href: '/destinations', icon: Upload },
    { name: 'Schedules', href: '/schedules', icon: Calendar },
    { name: 'Jobs', href: '/jobs', icon: FileText },
    { name: 'Users', href: '/users', icon: Users },
  ];

  function isActive(href: string) {
    return $page.url.pathname.startsWith(href);
  }
</script>

<div class="flex flex-col w-64 bg-white border-r border-supabase-gray-200">
  <div class="flex items-center h-16 px-6 border-b border-supabase-gray-200">
    <div class="flex items-center">
      <div class="w-8 h-8 bg-supabase-green rounded-lg flex items-center justify-center">
        <span class="text-white font-bold text-lg">C</span>
      </div>
      <span class="ml-3 text-xl font-semibold text-supabase-gray-900">Conductor</span>
    </div>
  </div>
  
  <nav class="flex-1 px-4 py-6 space-y-1">
    {#each navigation as item}
      
        href={item.href}
        class="flex items-center px-3 py-2 text-sm font-medium rounded-md transition-colors"
        class:bg-supabase-green={isActive(item.href)}
        class:text-white={isActive(item.href)}
        class:text-supabase-gray-700={!isActive(item.href)}
        class:hover:bg-supabase-gray-100={!isActive(item.href)}
      >
        <svelte:component this={item.icon} size={20} class="mr-3" />
        {item.name}
      </a>
    {/each}
  </nav>
</div>
