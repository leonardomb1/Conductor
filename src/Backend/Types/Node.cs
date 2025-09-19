namespace Conductor.Types;

public enum Node
{
    MasterPrincipal,
    MasterCluster,
    Worker,
    Single
}

public enum NodeStatus
{
    Unknown,
    Ready,
    Busy
}

public struct NodeInfo
{
    public string NodeId { get; set; }
    public Node NodeType { get; set; }
    public string Endpoint { get; set; }
};

public record NodeHealthResponse
{
    public string Status { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public double CpuUsage { get; init; }
    public int ActiveJobs { get; init; }
    public NodeStatus NodeStatus { get; init; }
}

public record NodeStatusInfo
{
    public string NodeId { get; init; } = "";
    public Node NodeType { get; init; }
    public string Endpoint { get; init; } = "";
    public NodeStatus Status { get; init; }
    public double CpuUsage { get; init; }
    public int ActiveJobs { get; init; }
    public DateTime LastChecked { get; init; }
}