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
      const success = await auth.login(username.toLowerCase(), password, loginType === "ldap")
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
            class="relative w-full bg-gradient-to-r from-supabase-green to-green-600 hover:from-green-600 hover:to-green-700 text-white font-semibold py-3 px-6 rounded-lg shadow-lg hover:shadow-xl transform hover:scale-[1.02] transition-all duration-200 ease-in-out focus:outline-none focus:ring-4 focus:ring-green-300 focus:ring-opacity-50 disabled:opacity-50 disabled:cursor-not-allowed disabled:transform-none"
            disabled={loading}
            onclick={handleLogin}
          >
            {#if loading}
              <div class="flex items-center justify-center">
                <svg
                  class="animate-spin -ml-1 mr-3 h-5 w-5 text-white"
                  xmlns="http://www.w3.org/2000/svg"
                  fill="none"
                  viewBox="0 0 24 24"
                >
                  <circle
                    class="opacity-25"
                    cx="12"
                    cy="12"
                    r="10"
                    stroke="currentColor"
                    stroke-width="4"
                  ></circle>
                  <path
                    class="opacity-75"
                    fill="currentColor"
                    d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                  ></path>
                </svg>
                <span>Signing in...</span>
              </div>
            {:else}
              <div class="flex items-center justify-center">
                <svg
                  class="w-5 h-5 mr-2"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M11 16l-4-4m0 0l4-4m-4 4h14m-5 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h7a3 3 0 013 3v1"
                  ></path>
                </svg>
                <span>Sign in</span>
              </div>
            {/if}
          </Button>
        </div>
      </div>
    </div>
  </div>
</div>
