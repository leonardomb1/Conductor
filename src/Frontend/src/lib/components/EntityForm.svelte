<script lang="ts">
  interface Props {
    entityType: 'user' | 'origin' | 'destination' | 'extraction' | 'schedule';
    data?: any;
    loading?: boolean;
    origins?: any[];
    destinations?: any[];
    extractions?: any[];
    submit: (formData: any) => void;
    cancel: () => void;
  }

  let {
    entityType,
    data = {},
    loading = false,
    origins = [],
    destinations = [],
    extractions = [],
    submit,
    cancel
  }: Props = $props();

  let formData = $state({ ...data })

  function handleSubmit(event: Event) {
    event.preventDefault()
    submit(formData)
  }

  function handleCancel() {
    cancel()
  }
</script>

<Card>
  <CardHeader>
    <CardTitle>
      {data.id ? 'Edit' : 'Create'} {entityType.charAt(0).toUpperCase() + entityType.slice(1)}
    </CardTitle>
  </CardHeader>
  <CardContent>
    <form onsubmit={handleSubmit} class="space-y-4">
      {#if entityType === 'user'}
        <div class="grid grid-cols-2 gap-4">
          <div class="space-y-2">
            <Label for="username">Username</Label>
            <Input id="username" bind:value={formData.username} required />
          </div>
          <div class="space-y-2">
            <Label for="email">Email</Label>
            <Input id="email" type="email" bind:value={formData.email} required />
          </div>
        </div>
        <div class="grid grid-cols-2 gap-4">
          <div class="space-y-2">
            <Label for="role">Role</Label>
            <Select
              id="role"
              bind:value={formData.role}
              options={[
                { value: 'admin', label: 'Administrator' },
                { value: 'user', label: 'User' },
                { value: 'viewer', label: 'Viewer' }
              ]}
            />
          </div>
          <div class="space-y-2">
            <Label for="isActive">Active</Label>
            <Switch bind:checked={formData.isActive} />
          </div>
        </div>
      {:else if entityType === 'origin'}
        <div class="space-y-2">
          <Label for="name">Name</Label>
          <Input id="name" bind:value={formData.name} required />
        </div>
        <div class="space-y-2">
          <Label for="description">Description</Label>
          <Textarea id="description" bind:value={formData.description} />
        </div>
        <div class="space-y-2">
          <Label for="sourceType">Source Type</Label>
          <Select
            id="sourceType"
            bind:value={formData.sourceType}
            options={[
              { value: 'db', label: 'Database' },
              { value: 'http', label: 'HTTP API' }
            ]}
          />
        </div>
        {#if formData.sourceType === 'db'}
          <div class="space-y-2">
            <Label for="connectionString">Connection String</Label>
            <Input id="connectionString" bind:value={formData.connectionString} />
          </div>
        {:else if formData.sourceType === 'http'}
          <div class="space-y-2">
            <Label for="url">URL</Label>
            <Input id="url" type="url" bind:value={formData.url} />
          </div>
        {/if}
        <div class="space-y-2">
          <Label for="isActive">Active</Label>
          <Switch bind:checked={formData.isActive} />
        </div>
      {:else if entityType === 'destination'}
        <div class="space-y-2">
          <Label for="name">Name</Label>
          <Input id="name" bind:value={formData.name} required />
        </div>
        <div class="space-y-2">
          <Label for="description">Description</Label>
          <Textarea id="description" bind:value={formData.description} />
        </div>
        <div class="space-y-2">
          <Label for="destinationType">Destination Type</Label>
          <Select
            id="destinationType"
            bind:value={formData.destinationType}
            options={[
              { value: 'db', label: 'Database' },
              { value: 'file', label: 'File' },
              { value: 'api', label: 'API' }
            ]}
          />
        </div>
        {#if formData.destinationType === 'db'}
          <div class="space-y-2">
            <Label for="connectionString">Connection String</Label>
            <Input id="connectionString" bind:value={formData.connectionString} />
          </div>
        {:else if formData.destinationType === 'file'}
          <div class="space-y-2">
            <Label for="filePath">File Path</Label>
            <Input id="filePath" bind:value={formData.filePath} />
          </div>
        {:else if formData.destinationType === 'api'}
          <div class="space-y-2">
            <Label for="apiEndpoint">API Endpoint</Label>
            <Input id="apiEndpoint" type="url" bind:value={formData.apiEndpoint} />
          </div>
        {/if}
        <div class="space-y-2">
          <Label for="isActive">Active</Label>
          <Switch bind:checked={formData.isActive} />
        </div>
      {:else if entityType === 'extraction'}
        <div class="space-y-2">
          <Label for="name">Name</Label>
          <Input id="name" bind:value={formData.name} required />
        </div>
        <div class="space-y-2">
          <Label for="description">Description</Label>
          <Textarea id="description" bind:value={formData.description} />
        </div>
        <div class="grid grid-cols-2 gap-4">
          <div class="space-y-2">
            <Label for="originId">Origin</Label>
            <Select
              id="originId"
              bind:value={formData.originId}
              options={origins.map(o => ({ value: o.id.toString(), label: o.name }))}
            />
          </div>
          <div class="space-y-2">
            <Label for="destinationId">Destination</Label>
            <Select
              id="destinationId"
              bind:value={formData.destinationId}
              options={destinations.map(d => ({ value: d.id.toString(), label: d.name }))}
            />
          </div>
        </div>
        <div class="space-y-2">
          <Label for="sourceType">Source Type</Label>
          <Select
            id="sourceType"
            bind:value={formData.sourceType}
            options={[
              { value: 'db', label: 'Database' },
              { value: 'http', label: 'HTTP API' }
            ]}
          />
        </div>
        {#if formData.sourceType === 'db'}
          <div class="space-y-2">
            <Label for="query">SQL Query</Label>
            <Textarea id="query" bind:value={formData.query} rows={4} />
          </div>
        {:else if formData.sourceType === 'http'}
          <div class="space-y-2">
            <Label for="endpoint">Endpoint</Label>
            <Input id="endpoint" bind:value={formData.endpoint} />
          </div>
          <div class="space-y-2">
            <Label for="httpMethod">HTTP Method</Label>
            <Select
              id="httpMethod"
              bind:value={formData.httpMethod}
              options={[
                { value: 'GET', label: 'GET' },
                { value: 'POST', label: 'POST' },
                { value: 'PUT', label: 'PUT' },
                { value: 'DELETE', label: 'DELETE' }
              ]}
            />
          </div>
        {/if}
        <div class="space-y-2">
          <Label for="isActive">Active</Label>
          <Switch bind:checked={formData.isActive} />
        </div>
      {:else if entityType === 'schedule'}
        <div class="space-y-2">
          <Label for="name">Name</Label>
          <Input id="name" bind:value={formData.name} required />
        </div>
        <div class="space-y-2">
          <Label for="description">Description</Label>
          <Textarea id="description" bind:value={formData.description} />
        </div>
        <div class="space-y-2">
          <Label for="extractionId">Extraction</Label>
          <Select
            id="extractionId"
            bind:value={formData.extractionId}
            options={extractions.map(e => ({ value: e.id.toString(), label: e.name }))}
          />
        </div>
        <div class="space-y-2">
          <Label for="cronExpression">Cron Expression</Label>
          <Input id="cronExpression" bind:value={formData.cronExpression} placeholder="0 0 * * *" />
        </div>
        <div class="space-y-2">
          <Label for="isActive">Active</Label>
          <Switch bind:checked={formData.isActive} />
        </div>
      {/if}
      
      <div class="flex justify-end space-x-2 pt-4">
        <Button type="button" variant="outline" onclick={handleCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={loading}>
          {loading ? 'Saving...' : 'Save'}
        </Button>
      </div>
    </form>
  </CardContent>
</Card>
