using System.Net;
using System.Net.Sockets;

namespace Bulb.Models
{
    public class TargetEndpoint{
        public TargetEndpoint(IPAddress address, short targetPort, bool isLocal = true)
        {
            Address = address;
            TargetPort = targetPort;
            IsLocal = isLocal;
        }

        public IPAddress Address { get; }
        public short TargetPort { get; }
        public bool IsLocal { get; set; }
        public bool IsIpv6 => Address.AddressFamily == AddressFamily.InterNetworkV6;
    }
}
