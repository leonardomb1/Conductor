namespace Conductor.Types;

public enum Node
{
    Master,
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

public record NodeHeartbeat(
    string NodeId,
    NodeStatus Status,
    int ActiveJobs,
    TimeSpan Uptime,
    Dictionary<string, object> Metrics
);
