using System.Text.RegularExpressions;
using Bulb.Models;

namespace Bulb.Util
{
    public static partial class NftablesUtil
    {
        private const string BulbComment = "bulb";

        public static IEnumerable<string> BuildDesiredRuleDefinitions(IEnumerable<BulbRule> rules)
        {
            foreach (var rule in rules)
            {
                ValidateRule(rule);

                var backends = rule.Backends
                    .OrderBy(backend => backend.Address.ToString(), StringComparer.Ordinal)
                    .ThenBy(backend => backend.TargetPort);
                if (!backends.Any())
                {
                    continue;
                }

                yield return BuildServiceRuleDefinition(rule, backends);

                foreach (var backend in backends.Where(backend => !backend.IsLocal))
                {
                    yield return BuildMasqueradeRuleDefinition(rule, backend);
                }
            }
        }

        public static void ValidateRule(BulbRule rule)
        {
            foreach (var backend in rule.Backends)
            {
                if (backend.IsIpv6 != rule.IsIpv6)
                {
                    throw new InvalidOperationException("Backend and service IP versions do not match.");
                }
            }

            if (!rule.IsTcp && !rule.IsUdp)
            {
                throw new InvalidOperationException("Only TCP and UDP protocols are supported.");
            }
        }

        public static void AddRule(string definition)
        {
            RunNft($"add rule inet bulb {definition}");
        }

        public static void DeleteRule(string definition)
        {
            RunNft($"delete rule inet bulb {definition}");
        }

        public static IEnumerable<string> GetExistingManagedRuleDefinitions()
        {
            var output = RunNft("-a list table inet bulb");
            var lines = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            string? currentChain = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (TryParseChainName(line, out var parsedChain))
                {
                    currentChain = parsedChain;
                    continue;
                }

                if (line == "}")
                {
                    currentChain = null;
                    continue;
                }

                if (currentChain == null || line.StartsWith("type ", StringComparison.Ordinal) || line.StartsWith("policy ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetRuleComment(line, out var comment) || !string.Equals(comment, BulbComment, StringComparison.Ordinal) || !line.Contains(" # handle ", StringComparison.Ordinal))
                {
                    continue;
                }

                var ruleWithoutHandle = StripHandleSuffix(line);
                yield return $"{currentChain} {ruleWithoutHandle}";
            }
        }

        public static void EnsureTableAndChains()
        {
            if (!CommandExists("list table inet bulb"))
            {
                RunNft("add table inet bulb");
            }

            if (!CommandExists("list chain inet bulb prerouting"))
            {
                RunNft("add chain inet bulb prerouting { type nat hook prerouting priority dstnat; policy accept; }");
            }

            if (!CommandExists("list chain inet bulb postrouting"))
            {
                RunNft("add chain inet bulb postrouting { type nat hook postrouting priority srcnat; policy accept; }");
            }
        }

        public static string BuildBackendDestination(TargetEndpoint backend)
        {
            return backend.IsIpv6 ? $"[{backend.Address}]:{backend.TargetPort}" : $"{backend.Address}:{backend.TargetPort}";
        }

        private static bool CommandExists(string args)
        {
            try
            {
                RunNft(args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildServiceRuleDefinition(BulbRule rule, IEnumerable<TargetEndpoint> backends)
        {
            var familyMatch = rule.IsIpv6 ? "ip6" : "ip";
            var protocolMatch = rule.IsTcp ? "tcp" : "udp";
            var backendTargets = string.Join(", ", backends.Select((backend, index) => $"{index} : {BuildBackendDestination(backend)}"));

            return $"prerouting {familyMatch} daddr {rule.LoadbalancerIp} {protocolMatch} dport {rule.LoadbalancerPort} dnat to numgen inc mod {backends.Count()} map {{ {backendTargets} }} comment \"{BulbComment}\"";
        }

        public static string BuildMasqueradeRuleDefinition(BulbRule rule, TargetEndpoint backend)
        {
            var familyMatch = rule.IsIpv6 ? "ip6" : "ip";
            var protocolMatch = rule.IsTcp ? "tcp" : "udp";
            var originalFamilyMatch = rule.IsIpv6 ? "ip6" : "ip";

            return $"postrouting {familyMatch} daddr {backend.Address} {protocolMatch} dport {backend.TargetPort} ct original {originalFamilyMatch} daddr {rule.LoadbalancerIp} ct original proto-dst {rule.LoadbalancerPort} masquerade comment \"{BulbComment}\"";
        }

        public static string BuildMasqueradeRuleDefinition(System.Net.IPAddress loadBalancerIp, short loadBalancerPort, System.Net.IPAddress backendIp, short backendPort, bool isTcp)
        {
            var familyMatch = loadBalancerIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "ip6" : "ip";
            var protocolMatch = isTcp ? "tcp" : "udp";
            var backendFamilyMatch = backendIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "ip6" : "ip";

            return $"postrouting {familyMatch} daddr {backendIp} {protocolMatch} dport {backendPort} ct original {backendFamilyMatch} daddr {loadBalancerIp} ct original proto-dst {loadBalancerPort} masquerade comment \"{BulbComment}\"";
        }

        private static bool TryParseChainName(string line, out string chainName)
        {
            var match = ChainNameRegex().Match(line);
            if (!match.Success)
            {
                chainName = string.Empty;
                return false;
            }

            chainName = match.Groups["name"].Value;
            return true;
        }

        private static bool TryGetRuleComment(string line, out string comment)
        {
            var match = RuleCommentRegex().Match(line);
            if (!match.Success)
            {
                comment = string.Empty;
                return false;
            }

            comment = match.Groups["comment"].Value;
            return true;
        }

        private static string StripHandleSuffix(string line)
        {
            return HandleSuffixRegex().Replace(line, string.Empty).Trim();
        }

        private static string RunNft(string args)
        {
            return ShellUtils.RunCommand("nft", args);
        }

        [GeneratedRegex(@"^chain\s+(?<name>\S+)\s*\{$")]
        private static partial Regex ChainNameRegex();

        [GeneratedRegex(@"comment\s+""(?<comment>[^""]+)""")]
        private static partial Regex RuleCommentRegex();

        [GeneratedRegex(@"\s+# handle \d+$")]
        private static partial Regex HandleSuffixRegex();
    }
}