using Conductor.Logging;
using Conductor.Service;
using Conductor.Shared;
using Conductor.Types;
using Serilog;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class NodeController(INodeRegistry nodeRegistry, IJobTracker jobTracker)
{
    private readonly INodeRegistry nodeRegistry = nodeRegistry;
    private readonly IJobTracker jobTracker = jobTracker;

    public async Task<IResult> RegisterNode(HttpRequest request)
    {
        var registrationResult = await Converter.TryDeserializeJson<NodeRegistrationRequest>(request.Body);
        
        if (!registrationResult.IsSuccessful)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid registration data.", true)
            );
        }

        var registrationRequest = registrationResult.Value;

        // Validate the registration request
        if (string.IsNullOrWhiteSpace(registrationRequest.NodeId) ||
            string.IsNullOrWhiteSpace(registrationRequest.Endpoint))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "NodeId and Endpoint are required.", true)
            );
        }

        // Validate endpoint format
        if (!Uri.TryCreate(registrationRequest.Endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid endpoint format. Must be a valid HTTP/HTTPS URL.", true)
            );
        }

        var nodeInfo = new NodeInfo
        {
            NodeId = registrationRequest.NodeId,
            NodeType = registrationRequest.NodeType,
            Endpoint = registrationRequest.Endpoint
        };

        try
        {
            await nodeRegistry.RegisterNodeAsync(nodeInfo);
            
            Log.Information($"Node {registrationRequest.NodeId} registered successfully with endpoint {registrationRequest.Endpoint}");
            
            return Results.Ok(
                new Message(Status200OK, $"Node {registrationRequest.NodeId} registered successfully.")
            );
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to register node {registrationRequest.NodeId}: {ex.Message}");
            return Results.InternalServerError(
                new Message(Status500InternalServerError, "Failed to register node.", true)
            );
        }
    }

    public async Task<IResult> GetNodes()
    {
        try
        {
            var nodes = await nodeRegistry.GetRegisteredNodesAsync();
            
            // Enhance with current status information
            var nodeStatuses = new List<NodeStatusInfo>();
            
            foreach (var node in nodes)
            {
                var status = await GetNodeCurrentStatus(node);
                nodeStatuses.Add(new NodeStatusInfo
                {
                    NodeId = node.NodeId,
                    NodeType = node.NodeType,
                    Endpoint = node.Endpoint,
                    Status = status.Status,
                    CpuUsage = status.CpuUsage,
                    ActiveJobs = status.ActiveJobs,
                    LastChecked = DateTime.UtcNow
                });
            }

            return Results.Ok(
                new Message<NodeStatusInfo>(Status200OK, "OK", nodeStatuses)
            );
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to get nodes: {ex.Message}");
            return Results.InternalServerError(
                new Message(Status500InternalServerError, "Failed to retrieve nodes.", true)
            );
        }
    }

    public async Task<IResult> RemoveNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "NodeId is required.", true)
            );
        }

        try
        {
            var nodes = await nodeRegistry.GetRegisteredNodesAsync();
            var existingNode = nodes.FirstOrDefault(n => n.NodeId == nodeId);

            if (existingNode.NodeId == null)
            {
                return Results.NotFound(
                    new Message(Status404NotFound, $"Node {nodeId} not found.", true)
                );
            }

            await nodeRegistry.RemoveNodeAsync(nodeId);
            
            Log.Information($"Node {nodeId} removed successfully");
            
            return Results.Ok(
                new Message(Status200OK, $"Node {nodeId} removed successfully.")
            );
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to remove node {nodeId}: {ex.Message}");
            return Results.InternalServerError(
                new Message(Status500InternalServerError, "Failed to remove node.", true)
            );
        }
    }

    public async Task<IResult> GetNodeStatus(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "NodeId is required.", true)
            );
        }

        try
        {
            var nodes = await nodeRegistry.GetRegisteredNodesAsync();
            var node = nodes.FirstOrDefault(n => n.NodeId == nodeId);

            if (node.NodeId == null)
            {
                return Results.NotFound(
                    new Message(Status404NotFound, $"Node {nodeId} not found.", true)
                );
            }

            var status = await GetNodeCurrentStatus(node);
            var nodeStatus = new NodeStatusInfo
            {
                NodeId = node.NodeId,
                NodeType = node.NodeType,
                Endpoint = node.Endpoint,
                Status = status.Status,
                CpuUsage = status.CpuUsage,
                ActiveJobs = status.ActiveJobs,
                LastChecked = DateTime.UtcNow
            };

            return Results.Ok(
                new Message<NodeStatusInfo>(Status200OK, "OK", [nodeStatus])
            );
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to get status for node {nodeId}: {ex.Message}");
            return Results.InternalServerError(
                new Message(Status500InternalServerError, "Failed to get node status.", true)
            );
        }
    }

    public IResult GetCurrentNodeHealth()
    {
        try
        {
            var cpuUsage = CpuUsage.GetCpuUsagePercent();
            var activeJobs = jobTracker.GetActiveJobs().Count();
            
            var health = new NodeHealthResponse
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                CpuUsage = cpuUsage,
                ActiveJobs = activeJobs,
                NodeStatus = cpuUsage >= Settings.MasterNodeCpuRedirectPercentage ? NodeStatus.Busy : NodeStatus.Ready
            };

            return Results.Ok(health);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to get current node health: {ex.Message}");
            return Results.InternalServerError(new
            {
                Status = "Unhealthy",
                Timestamp = DateTime.UtcNow,
                Error = ex.Message
            });
        }
    }

    private async Task<(NodeStatus Status, double CpuUsage, int ActiveJobs)> GetNodeCurrentStatus(NodeInfo node)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{node.Endpoint}/api/health");
            if (!response.IsSuccessStatusCode)
            {
                return (NodeStatus.Unknown, 0, 0);
            }

            var healthJson = await response.Content.ReadAsStringAsync();
            var health = System.Text.Json.JsonSerializer.Deserialize<NodeHealthResponse>(healthJson, Settings.JsonSOptions.Value);

            if (health == null)
            {
                return (NodeStatus.Unknown, 0, 0);
            }

            return (health.NodeStatus, health.CpuUsage, health.ActiveJobs);
        }
        catch
        {
            return (NodeStatus.Unknown, 0, 0);
        }
    }

    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/nodes")
            .WithTags("Node Management");

        group.MapPost("/register", async (NodeController controller, HttpRequest request) =>
            await controller.RegisterNode(request))
            .WithName("RegisterNode")
            .WithSummary("Register a new node with the cluster")
            .WithDescription("""
                Registers a new worker or master node with the cluster. The node will be added to the 
                registry and become available for load balancing.
                
                **Request Body:**
                ```json
                {
                    "nodeId": "worker-01",
                    "nodeType": "Worker",
                    "endpoint": "http://192.168.1.100:8080"
                }
                ```
                
                **Node Types:**
                - `Master`: Can distribute work and execute extractions
                - `Worker`: Only executes assigned extractions  
                - `Single`: Standalone node (both master and worker capabilities)
                
                The endpoint must be a valid HTTP/HTTPS URL that other nodes can reach.
                """)
            .Accepts<NodeRegistrationRequest>("application/json")
            .Produces<Message>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message>(Status500InternalServerError, "application/json");

        group.MapGet("/", async (NodeController controller) =>
            await controller.GetNodes())
            .WithName("GetNodes")
            .WithSummary("Get all registered nodes with their current status")
            .WithDescription("""
                Returns a list of all registered nodes in the cluster along with their current status information.
                
                **Response includes:**
                - Node identification (ID, type, endpoint)
                - Current CPU usage percentage
                - Number of active jobs
                - Node availability status (Ready/Busy/Unknown)
                - Last status check timestamp
                
                Status is determined by querying each node's health endpoint in real-time.
                """)
            .Produces<Message<NodeStatusInfo>>(Status200OK, "application/json")
            .Produces<Message>(Status500InternalServerError, "application/json");

        group.MapDelete("/{nodeId}", async (NodeController controller, string nodeId) =>
            await controller.RemoveNode(nodeId))
            .WithName("RemoveNode")
            .WithSummary("Remove a node from the cluster")
            .WithDescription("""
                Removes a node from the cluster registry. The node will no longer receive 
                work assignments through the load balancer.
                
                **Note:** This does not stop any currently running jobs on the node.
                """)
            .Produces<Message>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message>(Status404NotFound, "application/json")
            .Produces<Message>(Status500InternalServerError, "application/json");

        group.MapGet("/{nodeId}/status", async (NodeController controller, string nodeId) =>
            await controller.GetNodeStatus(nodeId))
            .WithName("GetNodeStatus")
            .WithSummary("Get detailed status for a specific node")
            .WithDescription("""
                Returns detailed status information for a specific registered node.
                
                **Status Information:**
                - CPU usage percentage
                - Number of active jobs  
                - Availability status
                - Last check timestamp
                """)
            .Produces<Message<NodeStatusInfo>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message>(Status404NotFound, "application/json")
            .Produces<Message>(Status500InternalServerError, "application/json");

        return group;
    }
}

// Supporting types
public record NodeRegistrationRequest(
    string NodeId,
    Node NodeType,
    string Endpoint
);

