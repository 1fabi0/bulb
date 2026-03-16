using System.Net;
using System.Text.RegularExpressions;

namespace Bulb.Util
{
    public static class BulbIpUtils
    {
        public static (IPAddress? Ip, short Port) SplitIpAndPort(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return (null, 0);

            string ipPart;
            string portPart;

            // Handle IPv6 with brackets: [2001:db8::1]:80
            if (input.StartsWith("["))
            {
                var match = IpPortRegex().Match(input);
                ipPart = match.Groups["ip"].Value;
                portPart = match.Groups["port"].Value;
            }
            // Handle IPv4 or IPv6 without brackets (standard ipvsadm -n output)
            else
            {
                int lastColon = input.LastIndexOf(':');
                if (lastColon > 0)
                {
                    ipPart = input.Substring(0, lastColon);
                    portPart = input.Substring(lastColon + 1);
                }
                else
                {
                    ipPart = input;
                    portPart = "0";
                }
            }

            IPAddress.TryParse(ipPart, out var address);
            short.TryParse(portPart, out short port);

            return (address, port);
        }

        [GeneratedRegex(@"^\[(?<ip>.+)\]:(?<port>\d+)$")]
        private static partial Regex IpPortRegex();
    }
}
