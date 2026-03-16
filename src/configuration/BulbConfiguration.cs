namespace Bulb.Configuration
{
    public class BulbConfiguration
    {
        public BulbConfiguration(bool isServiceStatusUpdateEnabled, string backendSystem, bool isEndpointRoutingEnabled, string nodeName, string? defaultScope)
        {
            IsServiceStatusUpdateEnabled = isServiceStatusUpdateEnabled;
            BackendSystem = backendSystem;
            IsEndpointRoutingEnabled = isEndpointRoutingEnabled;
            NodeName = nodeName;
            DefaultScope = defaultScope;
        }
        public bool IsServiceStatusUpdateEnabled { get; }
        public string BackendSystem { get; }
        public bool IsEndpointRoutingEnabled { get; }
        public string NodeName { get; }
        public string? DefaultScope { get; }
    }
}
