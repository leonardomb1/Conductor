<script lang="ts">
  import { onMount } from 'svelte'
  import { goto } from '$app/navigation'
  import Button from '$lib/components/ui/Button.svelte'
  import Input from '$lib/components/ui/Input.svelte'
  import Label from '$lib/components/ui/Label.svelte'
  import Card from '$lib/components/ui/Card.svelte'
  import CardHeader from '$lib/components/ui/CardHeader.svelte'
  import CardTitle from '$lib/components/ui/CardTitle.svelte'
  import CardContent from '$lib/components/ui/CardContent.svelte'
  import Alert from '$lib/components/ui/Alert.svelte'
  import AlertDescription from '$lib/components/ui/AlertDescription.svelte'
  import { AuthService } from '$lib/auth'
  import { toasts } from '$lib/stores/toast'
  import logo from '$lib/assets/logo.png'

  let username = $state('')
  let password = $state('')
  let loading = $state(false)
  let error = $state('')

  async function handleLogin() {
    if (!username || !password) {
      error = 'Please enter both username and passwor'
      return
    }

    loading = true
    error = ''

    try {
      const success = await AuthService.login(username, password)
      if (success) {
        toasts.add({
          type: 'success',
          title: 'Login successful',
          description: 'Welcome to Conductor!'
        })
        goto('/dashboard')
      } else {
        error = 'Invalid username or password'
      }
    } catch (err) {
      error = err instanceof Error ? err.message : 'Login failed'
    } finally {
      loading = false
    }
  }

  onMount(() => {
    // Redirect if already authenticated
    if (AuthService.isAuthenticated()) {
      goto('/dashboard')
    }
  })
</script>

<svelte:head>
  <title>Login - Conductor</title>
</svelte:head>

<div class="min-h-screen flex items-center justify-center bg-gray-50 py-12 px-4 sm:px-6 lg:px-8">
  <div class="max-w-md w-full space-y-8">
    <div class="text-center">
      <img src={logo || "/placeholder.svg"} alt="Conductor Logo" class="mx-auto h-16 w-16" />
      <h2 class="mt-6 text-3xl font-extrabold text-gray-900">
        Sign in to Conductor
      </h2>
      <p class="mt-2 text-sm text-gray-600">
        Data extraction and transformation platform
      </p>
    </div>

    <Card>
      <CardHeader>
        <CardTitle class="text-center">Login</CardTitle>
      </CardHeader>
      <CardContent>
        {#if error}
          <Alert variant="destructive" class="mb-4">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        {/if}

        <form onsubmit={handleLogin} class="space-y-4">
          <div class="space-y-2">
            <Label for="username">Username</Label>
            <Input
              id="username"
              type="text"
              bind:value={username}
              placeholder="Enter your username"
              required
              disabled={loading}
            />
          </div>

          <div class="space-y-2">
            <Label for="password">Password</Label>
            <Input
              id="password"
              type="password"
              bind:value={password}
              placeholder="Enter your password"
              required
              disabled={loading}
            />
          </div>

          <Button
            type="submit"
            class="w-full"
            disabled={loading}
          >
            {loading ? 'Signing in...' : 'Sign in'}
          </Button>
        </form>
      </CardContent>
    </Card>

    <div class="text-center text-sm text-gray-600">
      <p>Demo credentials:</p>
      <p>Username: <code class="bg-gray-100 px-1 rounded">admin</code></p>
      <p>Password: <code class="bg-gray-100 px-1 rounded">password</code></p>
    </div>
  </div>
</div>
