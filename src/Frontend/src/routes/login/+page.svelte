<script lang="ts">
  import { auth } from "$lib/auth.svelte.js"
  import { goto } from "$app/navigation"
  import Button from "$lib/components/ui/Button.svelte"
  import Input from "$lib/components/ui/Input.svelte"

  let username = $state("")
  let password = $state("")
  let loading = $state(false)
  let error = $state("")
  let loginType = $state<"local" | "ldap">("local")

  async function handleLogin() {
    if (!username || !password) {
      error = "Please enter both username and password"
      return
    }

    loading = true
    error = ""

    try {
      const success = await auth.login(username, password, loginType === "ldap")
      if (success) {
        goto("/dashboard")
      } else {
        error = "Invalid credentials"
      }
    } catch (err) {
      error = "Login failed. Please try again."
    } finally {
      loading = false
    }
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === "Enter") {
      handleLogin()
    }
  }
</script>

<svelte:head>
  <title>Login - Conductor</title>
</svelte:head>

<div
  class="min-h-screen flex items-center justify-center bg-supabase-gray-50 py-12 px-4 sm:px-6 lg:px-8"
>
  <div class="max-w-md w-full space-y-8">
    <div>
      <div
        class="mx-auto h-12 w-12 bg-supabase-green rounded-lg flex items-center justify-center"
      >
        <span class="text-white font-bold text-xl">C</span>
      </div>
      <h2 class="mt-6 text-center text-3xl font-bold text-supabase-gray-900">
        Sign in to Conductor
      </h2>
      <p class="mt-2 text-center text-sm text-supabase-gray-600">
        ETL Data Pipeline Management
      </p>
    </div>

    <div class="mt-8 space-y-6">
      <div class="bg-white p-6 rounded-lg shadow">
        <div class="space-y-4">
          <div class="flex rounded-md border border-supabase-gray-300">
            <button
              class="flex-1 px-4 py-2 text-sm font-medium rounded-l-md transition-colors"
              class:bg-supabase-green={loginType === "local"}
              class:text-white={loginType === "local"}
              class:text-supabase-gray-700={loginType !== "local"}
              class:bg-white={loginType !== "local"}
              onclick={() => (loginType = "local")}
            >
              Local
            </button>
            <button
              class="flex-1 px-4 py-2 text-sm font-medium rounded-r-md transition-colors"
              class:bg-supabase-green={loginType === "ldap"}
              class:text-white={loginType === "ldap"}
              class:text-supabase-gray-700={loginType !== "ldap"}
              class:bg-white={loginType !== "ldap"}
              onclick={() => (loginType = "ldap")}
            >
              LDAP
            </button>
          </div>

          <Input
            type="text"
            label="Username"
            bind:value={username}
            placeholder="Enter your username"
            required
            onkeydown={handleKeydown}
          />

          <Input
            type="password"
            label="Password"
            bind:value={password}
            placeholder="Enter your password"
            required
            onkeydown={handleKeydown}
          />

          {#if error}
            <div class="text-red-600 text-sm">{error}</div>
          {/if}

          <Button
            variant="primary"
            size="lg"
            class="bg-supabase-green text-white rounded-md w-full"
            {loading}
            onclick={handleLogin}
          >
            {loading ? "Signing in..." : "Sign in"}
          </Button>
        </div>
      </div>
    </div>
  </div>
</div>
