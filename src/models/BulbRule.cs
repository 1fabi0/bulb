using System.Net;

namespace Bulb.Models
{
    public class BulbRule
    {
        public BulbRule(IEnumerable<TargetEndpoint> backends, IPAddress loadbalancerIp, short loadbalancerPort, string protocol)
        {
            Backends = backends;
            LoadbalancerIp = loadbalancerIp;
            LoadbalancerPort = loadbalancerPort;
            Protocol = protocol;
        }
        private string Protocol { get; }
        public bool IsTcp => Protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase);
        public bool IsUdp => Protocol.Equals("UDP", StringComparison.OrdinalIgnoreCase);
        public IPAddress LoadbalancerIp {get; }
        public short LoadbalancerPort { get; }
        public IEnumerable<TargetEndpoint> Backends { get; }
        public bool IsIpv6 => LoadbalancerIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
    }
}