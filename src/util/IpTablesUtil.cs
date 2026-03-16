using System;
using Bulb.Models;

namespace Bulb.Util
{
    public static class IpTablesUtil
    {
        public static string RunIpTables(string args, bool isIpv6 = false)
        {
            if (isIpv6)
            {
                return ShellUtils.RunCommand("ip6tables", args);
            }
            return ShellUtils.RunCommand("iptables", args);
        }

        public static void ParseNatTableOutput(string output, IEnumerable<BulbRule> ipVsRules)
        {
            var lines = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                // Match lines like "SNAT       tcp  --  anywhere             anywhere             tcp dpt:80 to:  
                if (line.StartsWith("SNAT") && line.Contains("to:"))
                {
                    if (!line.EndsWith("/* bulb */"))
                    {
                        continue; // Not a rule added by Bulb, ignore
                    }
                    var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    var toPart = parts.FirstOrDefault(p => p.StartsWith("to:"));
                    var protPart = parts.FirstOrDefault(p => p == "6" || p == "17" || p == "0"); // 6 for TCP, 17 for UDP, 0 for any
                    if (toPart != null && protPart != null)
                    {
                        var toValue = toPart.Substring(3); // Remove "to:"
                        var (ip, port) = BulbIpUtils.SplitIpAndPort(toValue);
                        if (ip != null)
                        {
                            // Find the corresponding backend and mark it as non-local
                            foreach (var rule in ipVsRules)
                            {
                                if (protPart != "0" && (!rule.IsTcp && protPart == "6" || rule.IsTcp && protPart == "17"))
                                {
                                    continue; // Protocol doesn't match, skip
                                }
                                foreach (var backend in rule.Backends)
                                {
                                    if (backend.Address.Equals(ip) && backend.TargetPort == port )
                                    {
                                        backend.IsLocal = false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
