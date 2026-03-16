namespace Bulb.Models
{
    public class ServicePort
    {
        public ServicePort(int port, string protocol, IEnumerable<TargetEndpoint> backends)
        {
            Port = port;
            Protocol = protocol;
            Backends = backends;
        }

        public int Port { get; }
        private string Protocol { get; }
        public IEnumerable<TargetEndpoint> Backends { get; }
        public bool IsTcp => Protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase);
        public bool IsUdp => Protocol.Equals("UDP", StringComparison.OrdinalIgnoreCase);
    }
}