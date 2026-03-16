using System.Net;
using System.Net.Sockets;

namespace Bulb.Models
{
    public class ScopeNodeIp
    {
        public ScopeNodeIp(string scope, IPAddress address)
        {
            Scope = scope;
            Address = address;
        }

        public string Scope { get; }
        public IPAddress Address { get; }
        public bool IsIpv6 => Address.AddressFamily == AddressFamily.InterNetworkV6;
    }
}
