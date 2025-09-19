using System.Text.Json;
using Conductor.Types;

namespace Conductor.Shared;

public interface INodeRegistry
{
    Task<List<NodeInfo>> GetRegisteredNodesAsync();
    Task RegisterNodeAsync(NodeInfo node);
    Task RemoveNodeAsync(string nodeId);
    Task<NodeInfo?> GetNextAvailableNodeAsync();
}

public class FileBasedNodeRegistry : INodeRegistry
{
    private readonly string nodesFilePath;
    private readonly SemaphoreSlim fileLock = new(1, 1);
    private int currentNodeIndex = 0;
    
    public FileBasedNodeRegistry()
    {
        nodesFilePath = Path.Combine(Directory.GetCurrentDirectory(), "nodes.json");
    }

    public async Task<List<NodeInfo>> GetRegisteredNodesAsync()
    {
        await fileLock.WaitAsync();
        try
        {
            if (!File.Exists(nodesFilePath))
                return [];

            var json = await File.ReadAllTextAsync(nodesFilePath);
            return JsonSerializer.Deserialize<List<NodeInfo>>(json) ?? [];
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task RegisterNodeAsync(NodeInfo node)
    {
        await fileLock.WaitAsync();
        try
        {
            var nodes = await GetRegisteredNodesAsync();
            var existingIndex = nodes.FindIndex(n => n.NodeId == node.NodeId);
            
            if (existingIndex >= 0)
            {
                nodes[existingIndex] = node; // Update existing
            }
            else
            {
                nodes.Add(node); // Add new
            }

            var json = JsonSerializer.Serialize(nodes, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(nodesFilePath, json);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task<NodeInfo?> GetNextAvailableNodeAsync()
    {
        var nodes = await GetRegisteredNodesAsync();
        if (!nodes.Any()) return null;

        // Round robin selection
        var startIndex = Interlocked.Increment(ref currentNodeIndex) % nodes.Count;
        
        // Try each node starting from the round-robin position
        for (int i = 0; i < nodes.Count; i++)
        {
            var nodeIndex = (startIndex + i) % nodes.Count;
            var node = nodes[nodeIndex];
            
            var status = await CheckNodeStatus(node);
            if (status == NodeStatus.Ready)
            {
                return node;
            }
        }
        
        return null; // All nodes busy
    }

    private static async Task<NodeStatus> CheckNodeStatus(NodeInfo node)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5); // Quick health check
            
            var response = await client.GetAsync($"{node.Endpoint}/api/health");
            if (!response.IsSuccessStatusCode) return NodeStatus.Unknown;

            var healthJson = await response.Content.ReadAsStringAsync();
            var health = JsonSerializer.Deserialize<NodeHealthResponse>(healthJson);
            
            return health?.CpuUsage >= Settings.MasterNodeCpuRedirectPercentage 
                ? NodeStatus.Busy 
                : NodeStatus.Ready;
        }
        catch
        {
            return NodeStatus.Unknown;
        }
    }

    public async Task RemoveNodeAsync(string nodeId)
    {
        await fileLock.WaitAsync();
        try
        {
            var nodes = await GetRegisteredNodesAsync();
            nodes.RemoveAll(n => n.NodeId == nodeId);
            
            var json = JsonSerializer.Serialize(nodes, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(nodesFilePath, json);
        }
        finally
        {
            fileLock.Release();
        }
    }
}