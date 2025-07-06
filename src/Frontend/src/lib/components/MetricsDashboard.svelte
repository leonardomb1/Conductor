<script lang="ts">
  import { onMount, onDestroy } from "svelte"
  import {
    Card,
    CardContent,
    CardHeader,
    CardTitle,
  } from "$lib/components/ui/Card.svelte"
  import { Badge } from "$lib/components/ui/Badge.svelte"
  import { Progress } from "$lib/components/ui/Progress.svelte"
  import { Alert, AlertDescription } from "$lib/components/ui/Alert.svelte"
  import {
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
  } from "$lib/components/ui/Tabs.svelte"
  import { AuthService } from "$lib/auth"

  interface ParsedMetric {
    name: string
    type: "counter" | "gauge" | "histogram" | "summary"
    help: string
    samples: Array<{
      labels: Record<string, string>
      value: number
      timestamp?: number
    }>
  }

  let metrics: ParsedMetric[] = $state([])
  let loading = $state(true)
  let error = $state("")
  let lastUpdated = $state(new Date())
  let refreshInterval: NodeJS.Timeout

  const parsePrometheusMetrics = (text: string): ParsedMetric[] => {
    const lines = text.split("\n")
    const metricsMap = new Map<string, ParsedMetric>()
    const helpMap = new Map<string, string>()
    const typeMap = new Map<string, string>()

    // First pass: collect HELP and TYPE comments
    for (const line of lines) {
      const trimmed = line.trim()
      if (trimmed.startsWith("# HELP ")) {
        const [, metricName, ...helpParts] = trimmed.split(" ")
        helpMap.set(metricName, helpParts.join(" "))
      } else if (trimmed.startsWith("# TYPE ")) {
        const [, metricName, type] = trimmed.split(" ")
        typeMap.set(metricName, type)
      }
    }

    // Second pass: parse metric samples
    for (const line of lines) {
      const trimmed = line.trim()
      if (!trimmed || trimmed.startsWith("#")) continue

      try {
        // Parse metric line: metric_name{labels} value [timestamp]
        const match = trimmed.match(
          /^([a-zA-Z_:][a-zA-Z0-9_:]*(?:\{[^}]*\})?) ([+-]?[0-9]*\.?[0-9]+(?:[eE][+-]?[0-9]+)?)(?:\s+([0-9]+))?$/,
        )

        if (!match) continue

        const [, metricWithLabels, valueStr, timestampStr] = match
        const value = parseFloat(valueStr)
        const timestamp = timestampStr ? parseInt(timestampStr) : Date.now()

        // Extract metric name and labels
        let metricName: string
        let labels: Record<string, string> = {}

        const labelsMatch = metricWithLabels.match(/^([^{]+)(?:\{([^}]*)\})?$/)
        if (labelsMatch) {
          metricName = labelsMatch[1]
          const labelsStr = labelsMatch[2]

          if (labelsStr) {
            // Parse labels: key1="value1",key2="value2"
            const labelMatches = labelsStr.matchAll(/(\w+)="([^"]*?)"/g)
            for (const labelMatch of labelMatches) {
              labels[labelMatch[1]] = labelMatch[2]
            }
          }
        } else {
          metricName = metricWithLabels
        }

        // Determine base metric name (remove suffixes for histograms/summaries)
        const baseMetricName = metricName.replace(
          /_(total|count|sum|bucket|created)$/,
          "",
        )

        if (!metricsMap.has(baseMetricName)) {
          metricsMap.set(baseMetricName, {
            name: baseMetricName,
            type: determineMetricType(metricName, typeMap.get(baseMetricName)),
            help: helpMap.get(baseMetricName) || `Metric: ${baseMetricName}`,
            samples: [],
          })
        }

        const metric = metricsMap.get(baseMetricName)!
        metric.samples.push({ labels, value, timestamp })
      } catch (err) {
        console.warn("Failed to parse metric line:", trimmed, err)
      }
    }

    return Array.from(metricsMap.values())
  }

  const determineMetricType = (
    metricName: string,
    declaredType?: string,
  ): "counter" | "gauge" | "histogram" | "summary" => {
    if (declaredType) {
      return declaredType as "counter" | "gauge" | "histogram" | "summary"
    }

    if (
      metricName.includes("_bucket") ||
      metricName.includes("_count") ||
      metricName.includes("_sum")
    ) {
      return "histogram"
    }
    if (metricName.endsWith("_total") || metricName.includes("_created")) {
      return "counter"
    }
    return "gauge"
  }

  const fetchMetrics = async () => {
    try {
      const response = await AuthService.authenticatedFetch("/api/metrics")
      if (!response.ok) throw new Error("Failed to fetch metrics")

      const text = await response.text()
      metrics = parsePrometheusMetrics(text)
      lastUpdated = new Date()
      error = ""
    } catch (err) {
      if (err instanceof Error && err.message !== "Authentication expired") {
        error = "Failed to load metrics data"
        console.error("Metrics fetch error:", err)
      }
    } finally {
      loading = false
    }
  }

  onMount(() => {
    fetchMetrics()
    refreshInterval = setInterval(fetchMetrics, 30000) // Refresh every 30 seconds
  })

  onDestroy(() => {
    if (refreshInterval) {
      clearInterval(refreshInterval)
    }
  })

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return "0 Bytes"
    const k = 1024
    const sizes = ["Bytes", "KB", "MB", "GB", "TB"]
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i]
  }

  const formatDuration = (seconds: number) => {
    if (seconds < 1) return `${(seconds * 1000).toFixed(2)}ms`
    if (seconds < 60) return `${seconds.toFixed(2)}s`
    const minutes = Math.floor(seconds / 60)
    const remainingSeconds = seconds % 60
    return `${minutes}m ${remainingSeconds.toFixed(0)}s`
  }

  const getMetricsByCategory = (category: string) => {
    return metrics.filter((m) => {
      const name = m.name.toLowerCase()
      switch (category) {
        case "http":
          return (
            name.includes("http") ||
            name.includes("request") ||
            name.includes("response")
          )
        case "runtime":
          return (
            name.includes("dotnet") ||
            name.includes("gc") ||
            name.includes("jit") ||
            name.includes("threadpool") ||
            name.includes("runtime") ||
            name.includes("clr")
          )
        case "process":
          return (
            name.includes("process") ||
            name.includes("cpu") ||
            name.includes("memory") ||
            name.includes("working_set") ||
            name.includes("virtual_memory")
          )
        case "otel":
          return (
            name.includes("otel") ||
            name.includes("opentelemetry") ||
            name.includes("trace") ||
            name.includes("span") ||
            name.includes("metric") ||
            name.includes("log")
          )
        default:
          return true
      }
    })
  }

  const formatMetricValue = (value: number, metricName: string) => {
    if (
      metricName.includes("bytes") ||
      metricName.includes("memory") ||
      metricName.includes("size")
    ) {
      return formatBytes(value)
    }
    if (
      metricName.includes("duration") ||
      metricName.includes("time") ||
      metricName.includes("seconds")
    ) {
      return formatDuration(value)
    }
    if (metricName.includes("ratio") || metricName.includes("percentage")) {
      return `${(value * 100).toFixed(2)}%`
    }
    if (value > 1000000) {
      return `${(value / 1000000).toFixed(2)}M`
    }
    if (value > 1000) {
      return `${(value / 1000).toFixed(2)}K`
    }
    return value.toLocaleString()
  }

  const getSystemMetrics = () => {
    const processMetrics = getMetricsByCategory("process")
    const cpuMetric = processMetrics.find(
      (m) => m.name.includes("cpu") && m.name.includes("usage"),
    )
    const memoryMetric = processMetrics.find(
      (m) => m.name.includes("memory") || m.name.includes("working_set"),
    )
    const uptimeMetric = processMetrics.find(
      (m) => m.name.includes("uptime") || m.name.includes("start_time"),
    )

    return {
      cpu: cpuMetric?.samples[0]?.value || 0,
      memory: memoryMetric?.samples[0]?.value || 0,
      uptime: uptimeMetric?.samples[0]?.value || 0,
    }
  }

  const getHttpMetrics = () => {
    const httpMetrics = getMetricsByCategory("http")
    const requestsMetric = httpMetrics.find(
      (m) => m.name.includes("request") && m.name.includes("total"),
    )
    const durationsMetric = httpMetrics.find(
      (m) => m.name.includes("duration") || m.name.includes("time"),
    )

    const totalRequests =
      requestsMetric?.samples.reduce((sum, s) => sum + s.value, 0) || 0
    const avgDuration =
      durationsMetric?.samples.reduce((sum, s) => sum + s.value, 0) /
        (durationsMetric?.samples.length || 1) || 0

    return {
      totalRequests,
      avgDuration,
      requestsPerSecond: totalRequests / 60, // rough estimate
    }
  }

  $: systemMetrics = getSystemMetrics()
  $: httpMetrics = getHttpMetrics()
</script>

<div class="space-y-6">
  <div class="flex items-center justify-between">
    <h2 class="text-2xl font-bold">OpenTelemetry Metrics</h2>
    <div class="text-sm text-muted-foreground">
      Last updated: {lastUpdated.toLocaleTimeString()}
    </div>
  </div>

  {#if error}
    <Alert variant="destructive">
      <AlertDescription>{error}</AlertDescription>
    </Alert>
  {/if}

  {#if loading}
    <div class="flex items-center justify-center py-8">Loading metrics...</div>
  {:else}
    <Tabs defaultValue="overview" class="w-full">
      <TabsList>
        <TabsTrigger value="overview">Overview</TabsTrigger>
        <TabsTrigger value="http">HTTP</TabsTrigger>
        <TabsTrigger value="runtime">.NET Runtime</TabsTrigger>
        <TabsTrigger value="process">Process</TabsTrigger>
        <TabsTrigger value="otel">OpenTelemetry</TabsTrigger>
        <TabsTrigger value="all">All Metrics</TabsTrigger>
      </TabsList>

      <TabsContent value="overview" class="space-y-6">
        <div class="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          <Card>
            <CardHeader
              class="flex flex-row items-center justify-between space-y-0 pb-2"
            >
              <CardTitle class="text-sm font-medium">CPU Usage</CardTitle>
              <svg
                class="h-4 w-4 text-muted-foreground"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M9 3v2m6-2v2M9 19v2m6-2v2M5 9H3m2 6H3m18-6h-2m2 6h-2M7 19h10a2 2 0 002-2V7a2 2 0 00-2-2H7a2 2 0 00-2 2v10a2 2 0 002 2zM9 9h6v6H9V9z"
                />
              </svg>
            </CardHeader>
            <CardContent>
              <div class="text-2xl font-bold">
                {(systemMetrics.cpu * 100).toFixed(1)}%
              </div>
              <Progress value={systemMetrics.cpu * 100} class="mt-2" />
              <p class="text-xs text-muted-foreground mt-1">
                Process CPU utilization
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader
              class="flex flex-row items-center justify-between space-y-0 pb-2"
            >
              <CardTitle class="text-sm font-medium">Memory Usage</CardTitle>
              <svg
                class="h-4 w-4 text-muted-foreground"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4"
                />
              </svg>
            </CardHeader>
            <CardContent>
              <div class="text-2xl font-bold">
                {formatBytes(systemMetrics.memory)}
              </div>
              <p class="text-xs text-muted-foreground mt-1">
                Working set memory
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader
              class="flex flex-row items-center justify-between space-y-0 pb-2"
            >
              <CardTitle class="text-sm font-medium">HTTP Requests</CardTitle>
              <svg
                class="h-4 w-4 text-muted-foreground"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M9 19l3 3m0 0l3-3m-3 3V10"
                />
              </svg>
            </CardHeader>
            <CardContent>
              <div class="text-2xl font-bold">
                {formatMetricValue(httpMetrics.totalRequests, "requests")}
              </div>
              <p class="text-xs text-muted-foreground mt-1">
                Total requests processed
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader
              class="flex flex-row items-center justify-between space-y-0 pb-2"
            >
              <CardTitle class="text-sm font-medium">Response Time</CardTitle>
              <svg
                class="h-4 w-4 text-muted-foreground"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"
                />
              </svg>
            </CardHeader>
            <CardContent>
              <div class="text-2xl font-bold">
                {formatDuration(httpMetrics.avgDuration)}
              </div>
              <p class="text-xs text-muted-foreground mt-1">
                Average response time
              </p>
            </CardContent>
          </Card>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Metrics Summary</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="grid gap-2 md:grid-cols-2 lg:grid-cols-4">
              <div class="text-center">
                <div class="text-2xl font-bold">{metrics.length}</div>
                <div class="text-sm text-muted-foreground">Total Metrics</div>
              </div>
              <div class="text-center">
                <div class="text-2xl font-bold">
                  {metrics.filter((m) => m.type === "counter").length}
                </div>
                <div class="text-sm text-muted-foreground">Counters</div>
              </div>
              <div class="text-center">
                <div class="text-2xl font-bold">
                  {metrics.filter((m) => m.type === "gauge").length}
                </div>
                <div class="text-sm text-muted-foreground">Gauges</div>
              </div>
              <div class="text-center">
                <div class="text-2xl font-bold">
                  {metrics.filter((m) => m.type === "histogram").length}
                </div>
                <div class="text-sm text-muted-foreground">Histograms</div>
              </div>
            </div>
          </CardContent>
        </Card>
      </TabsContent>

      {#each ["http", "runtime", "process", "otel"] as category}
        <TabsContent value={category}>
          {@const categoryMetrics = getMetricsByCategory(category)}
          <Card>
            <CardHeader>
              <CardTitle class="capitalize"
                >{category === "otel" ? "OpenTelemetry" : category} Metrics</CardTitle
              >
            </CardHeader>
            <CardContent>
              <div class="space-y-4">
                {#each categoryMetrics as metric}
                  <div class="border rounded-lg p-4">
                    <div class="flex items-center justify-between mb-2">
                      <h3 class="font-medium">{metric.name}</h3>
                      <div class="flex gap-2">
                        <Badge variant="outline">{metric.type}</Badge>
                        <Badge variant="secondary"
                          >{metric.samples.length} samples</Badge
                        >
                      </div>
                    </div>
                    <p class="text-sm text-muted-foreground mb-3">
                      {metric.help}
                    </p>

                    <div class="space-y-2">
                      {#each metric.samples.slice(0, 3) as sample}
                        <div
                          class="flex items-center justify-between text-sm bg-muted/50 rounded p-2"
                        >
                          <div class="flex gap-1 flex-wrap">
                            {#each Object.entries(sample.labels) as [key, value]}
                              <Badge variant="secondary" class="text-xs"
                                >{key}={value}</Badge
                              >
                            {/each}
                          </div>
                          <div class="font-mono font-bold">
                            {formatMetricValue(sample.value, metric.name)}
                          </div>
                        </div>
                      {/each}
                      {#if metric.samples.length > 3}
                        <div class="text-xs text-muted-foreground text-center">
                          ... and {metric.samples.length - 3} more samples
                        </div>
                      {/if}
                    </div>
                  </div>
                {/each}

                {#if categoryMetrics.length === 0}
                  <div class="text-center py-8 text-muted-foreground">
                    No {category} metrics available
                  </div>
                {/if}
              </div>
            </CardContent>
          </Card>
        </TabsContent>
      {/each}

      <TabsContent value="all">
        <Card>
          <CardHeader>
            <CardTitle>All Metrics ({metrics.length})</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="space-y-3 max-h-[600px] overflow-y-auto">
              {#each metrics as metric}
                <div class="border rounded-lg p-3">
                  <div class="flex items-center justify-between mb-2">
                    <h3 class="font-medium text-sm">{metric.name}</h3>
                    <div class="flex gap-1">
                      <Badge variant="outline" class="text-xs"
                        >{metric.type}</Badge
                      >
                      <Badge variant="secondary" class="text-xs"
                        >{metric.samples.length}</Badge
                      >
                    </div>
                  </div>
                  <div class="text-xs text-muted-foreground mb-2">
                    {metric.help}
                  </div>
                  <div class="text-sm">
                    {#if metric.samples.length > 0}
                      Latest: <span class="font-mono font-bold"
                        >{formatMetricValue(
                          metric.samples[0].value,
                          metric.name,
                        )}</span
                      >
                    {/if}
                  </div>
                </div>
              {/each}
            </div>
          </CardContent>
        </Card>
      </TabsContent>
    </Tabs>
  {/if}
</div>
