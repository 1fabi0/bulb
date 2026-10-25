using System.Net;
using System.Net.Sockets;

namespace Bulb.Models
{
    public class ScopeNodeIp
    {
        public ScopeNodeIp(string scope, IPAddress address, string? nodeName = null)
        {
            Scope = scope;
            Address = address;
            NodeName = nodeName;
        }

        public string? NodeName { get; }
        public string Scope { get; }
        public IPAddress Address { get; }
        public bool IsIpv6 => Address.AddressFamily == AddressFamily.InterNetworkV6;
    }
}
