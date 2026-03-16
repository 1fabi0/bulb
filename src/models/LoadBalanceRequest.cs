namespace Bulb.Models
{
    public class LoadBalanceRequest 
    {
        public LoadBalanceRequest(string serviceName, string serviceNamespace, IEnumerable<ServicePort> servicePorts, ScopeNodeIp nodeInfo)
        {
            ServiceName = serviceName;
            ServiceNamespace = serviceNamespace;
            ServicePorts = servicePorts;
            NodeInfo = nodeInfo;
        }
        public string ServiceName { get; }
        public string ServiceNamespace { get; }
        public IEnumerable<ServicePort> ServicePorts { get; }
        public ScopeNodeIp NodeInfo { get; }
        
    }
}
