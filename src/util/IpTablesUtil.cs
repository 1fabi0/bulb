using System;
using System.Net;
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
                if (!(line.StartsWith("SNAT") || line.StartsWith("MASQUERADE")))
                {
                    continue;
                }

                if (!line.Contains("/* bulb */"))
                {
                    continue;
                }

                var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    continue;
                }

                var destinationPart = parts[4];
                var dptPart = parts.FirstOrDefault(p => p.StartsWith("dpt:", StringComparison.Ordinal));
                if (!IPAddress.TryParse(destinationPart, out var destinationIp) || dptPart == null)
                {
                    continue;
                }

                if (!short.TryParse(dptPart.Substring(4), out var destinationPort))
                {
                    continue;
                }

                var virtualAddressPart = GetTokenValue(parts, "vaddr", "--vaddr");
                var virtualPortPart = GetTokenValue(parts, "vport", "--vport");
                if (!TryParseAddressToken(virtualAddressPart, out var virtualAddress) || !short.TryParse(virtualPortPart, out var virtualPort))
                {
                    continue;
                }

                var protPart = parts[1];
                foreach (var rule in ipVsRules)
                {
                    if (!IsProtocolMatch(protPart, rule))
                    {
                        continue;
                    }

                    if (!rule.LoadbalancerIp.Equals(virtualAddress) || rule.LoadbalancerPort != virtualPort)
                    {
                        continue;
                    }

                    foreach (var backend in rule.Backends)
                    {
                        if (backend.Address.Equals(destinationIp) && backend.TargetPort == destinationPort)
                        {
                            backend.IsLocal = false;
                        }
                    }
                }
            }
        }

        private static string? GetTokenValue(string[] parts, params string[] tokenNames)
        {
            for (var index = 0; index < parts.Length; index++)
            {
                foreach (var tokenName in tokenNames)
                {
                    if (string.Equals(parts[index], tokenName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (index + 1 < parts.Length)
                        {
                            return parts[index + 1];
                        }

                        return null;
                    }

                    var prefix = tokenName + ":";
                    if (parts[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return parts[index].Substring(prefix.Length);
                    }
                }
            }

            return null;
        }

        private static bool TryParseAddressToken(string? addressToken, out IPAddress parsedAddress)
        {
            if (string.IsNullOrWhiteSpace(addressToken))
            {
                parsedAddress = IPAddress.None;
                return false;
            }

            var slashIndex = addressToken.IndexOf('/');
            var normalizedAddress = slashIndex >= 0 ? addressToken.Substring(0, slashIndex) : addressToken;
            if (IPAddress.TryParse(normalizedAddress, out var candidateAddress))
            {
                parsedAddress = candidateAddress;
                return true;
            }

            parsedAddress = IPAddress.None;
            return false;
        }

        private static bool IsProtocolMatch(string protocol, BulbRule rule)
        {
            if (string.Equals(protocol, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "all", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(protocol, "6", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "tcp", StringComparison.OrdinalIgnoreCase))
            {
                return rule.IsTcp;
            }

            if (string.Equals(protocol, "17", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "udp", StringComparison.OrdinalIgnoreCase))
            {
                return rule.IsUdp;
            }

            return false;
        }
    }
}
