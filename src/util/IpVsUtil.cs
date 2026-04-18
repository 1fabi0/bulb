using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Bulb.Models;

namespace Bulb.Util
{
    public partial class IpVsUtil
    {
        public static string RunIpvsAdm(string args)
        {
            return ShellUtils.RunCommand("ipvsadm", args);
        }
        public static IEnumerable<BulbRule> ParseIpVsAdmOutput(string output)
        {
            var lines = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            var rules = new List<BulbRule>();

            IPAddress? currentServiceIp = null;
            short currentServicePort = 0;
            string currentProtocol = string.Empty;
            List<TargetEndpoint> currentBackends = new List<TargetEndpoint>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                // 1. Match Virtual Service
                if (line.StartsWith("TCP") || line.StartsWith("UDP"))
                {
                    var parsedProtocol = line.Substring(0, 3).Trim();
                    // Flush the previous rule before starting a new one
                    if (currentServiceIp != null)
                    {
                        rules.Add(new BulbRule(currentBackends, currentServiceIp, currentServicePort, currentProtocol));
                    }

                    var parts = WhiteSpaceRegex().Split(line);
                    var (ip, port) = BulbIpUtils.SplitIpAndPort(parts[1]);

                    currentServiceIp = ip;
                    currentServicePort = port;
                    currentProtocol = parsedProtocol;
                    currentBackends = new List<TargetEndpoint>();
                }
                // 2. Match Real Server (e.g., "-> 192.168.1.10:8080 Masq 1 0 0")
                else if (line.StartsWith("->"))
                {
                    var parts = WhiteSpaceRegex().Split(line);
                    var (ip, port) = BulbIpUtils.SplitIpAndPort(parts[1]);

                    if (ip != null)
                    {
                        currentBackends.Add(new TargetEndpoint(ip, port));
                    }
                }
            }

            // Final flush for the last entry in the output
            if (currentServiceIp != null)
            {
                rules.Add(new BulbRule(currentBackends, currentServiceIp, currentServicePort, currentProtocol));
            }

            return rules;
        }
        [GeneratedRegex(@"\s+")]
        private static partial Regex WhiteSpaceRegex();
    }
}