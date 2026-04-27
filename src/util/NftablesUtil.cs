using System.Text.Json;
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
                    .ThenBy(backend => backend.TargetPort)
                    .ToArray();
                if (backends.Length == 0)
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
            if (!TryDeleteRuleByHandle(definition))
            {
                throw new InvalidOperationException($"Managed rule not found for deletion: {definition}");
            }
        }

        public static IEnumerable<string> GetExistingManagedRuleDefinitions()
        {
            var output = RunNft("-j -a list table inet bulb");
            return ParseManagedRuleDefinitionsFromJson(output);
        }

        public static IEnumerable<string> ParseManagedRuleDefinitionsFromJson(string output)
        {
            using var document = JsonDocument.Parse(output);

            if (!document.RootElement.TryGetProperty("nftables", out var nftables) || nftables.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Unexpected nft JSON output: expected a root 'nftables' array when listing table 'inet bulb'. Output: {output}");
            }

            var managedRuleDefinitions = new List<string>();

            foreach (var element in nftables.EnumerateArray())
            {
                if (!element.TryGetProperty("rule", out var rule))
                {
                    continue;
                }

                if (!TryGetRuleComment(rule, out var comment) || !string.Equals(comment, BulbComment, StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryBuildManagedRuleDefinition(rule, out var ruleDefinition))
                {
                    managedRuleDefinitions.Add(ruleDefinition);
                }
            }

            return managedRuleDefinitions;
        }

        private static bool TryDeleteRuleByHandle(string definition)
        {
            var output = RunNft("-j -a list table inet bulb");
            using var document = JsonDocument.Parse(output);


            if (!document.RootElement.TryGetProperty("nftables", out var nftables) || nftables.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var element in nftables.EnumerateArray())
            {
                if (!element.TryGetProperty("rule", out var rule))
                {
                    continue;
                }

                if (!TryGetRuleComment(rule, out var comment) || !string.Equals(comment, BulbComment, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryBuildManagedRuleDefinition(rule, out var currentDefinition) || !string.Equals(currentDefinition, definition, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetStringProperty(rule, "chain", out var chainName) || !TryGetProperty(rule, "handle", out var handleElement) || !TryGetInt32Value(handleElement, out var handle))
                {
                    continue;
                }

                RunNft($"delete rule inet bulb {chainName} handle {handle}");
                return true;
            }

            return false;
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

        private static string BuildBackendMapDestination(TargetEndpoint backend)
        {
            return $"{backend.Address} . {backend.TargetPort}";
        }

        private static bool TryBuildManagedRuleDefinition(JsonElement rule, out string ruleDefinition)
        {
            ruleDefinition = string.Empty;

            if (!TryGetStringProperty(rule, "chain", out var chainName))
            {
                return false;
            }

            if (!TryGetArrayProperty(rule, "expr", out var expressions) || expressions.Length == 0)
            {
                return false;
            }

            if (TryBuildManagedServiceRuleDefinition(chainName, expressions, out ruleDefinition))
            {
                return true;
            }

            return TryBuildManagedMasqueradeRuleDefinition(chainName, expressions, out ruleDefinition);
        }

        private static bool TryGetRuleComment(JsonElement rule, out string comment)
        {
            comment = string.Empty;

            if (!TryGetStringProperty(rule, "comment", out comment))
            {
                return false;
            }

            return true;
        }

        private static bool TryBuildManagedServiceRuleDefinition(string chainName, JsonElement[] expressions, out string ruleDefinition)
        {
            ruleDefinition = string.Empty;

            if (expressions.Length < 3)
            {
                return false;
            }

            if (!TryReadPayloadMatch(expressions[0], "daddr", out var familyMatch, out var loadBalancerIp))
            {
                return false;
            }

            if (!TryReadPayloadMatch(expressions[1], "dport", out var protocolMatch, out var loadBalancerPort))
            {
                return false;
            }

            if (!TryReadDnatStatement(expressions[^1], out var dnatFamilyMatch, out var backendTargetMap))
            {
                return false;
            }

            ruleDefinition = $"{chainName} {familyMatch} daddr {loadBalancerIp} {protocolMatch} dport {loadBalancerPort} dnat {dnatFamilyMatch} to numgen inc mod {backendTargetMap.Length} map {{ {string.Join(", ", backendTargetMap.Select(backendTarget => $"{backendTarget.Key} : {backendTarget.Destination}"))} }} comment \"{BulbComment}\"";
            return true;
        }

        private static bool TryBuildManagedMasqueradeRuleDefinition(string chainName, JsonElement[] expressions, out string ruleDefinition)
        {
            ruleDefinition = string.Empty;

            if (expressions.Length < 5)
            {
                return false;
            }

            if (!TryReadPayloadMatch(expressions[0], "daddr", out var familyMatch, out var backendIp))
            {
                return false;
            }

            if (!TryReadPayloadMatch(expressions[1], "dport", out var protocolMatch, out var backendPort))
            {
                return false;
            }

            if (!TryReadCtMatch(expressions[2], "daddr", out var originalFamilyMatch, out var loadBalancerIp))
            {
                return false;
            }

            if (!TryReadCtMatch(expressions[3], "proto-dst", out _, out var loadBalancerPort))
            {
                return false;
            }

            if (!IsNamedStatement(expressions[^1], "masquerade"))
            {
                return false;
            }

            if (string.IsNullOrEmpty(originalFamilyMatch))
            {
                originalFamilyMatch = familyMatch;
            }

            ruleDefinition = $"{chainName} {familyMatch} daddr {backendIp} {protocolMatch} dport {backendPort} ct original {originalFamilyMatch} daddr {loadBalancerIp} ct original proto-dst {loadBalancerPort} masquerade comment \"{BulbComment}\"";
            return true;
        }

        private static bool TryReadDnatStatement(JsonElement statement, out string familyMatch, out (int Key, string Destination)[] backendTargetMap)
        {
            familyMatch = string.Empty;
            backendTargetMap = [];

            if (!statement.TryGetProperty("dnat", out var dnat) || dnat.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetStringProperty(dnat, "family", out familyMatch))
            {
                return false;
            }

            if (!TryGetProperty(dnat, "addr", out var addr) || !TryGetProperty(addr, "map", out var map) || !TryGetProperty(map, "data", out var data) || !TryGetProperty(data, "set", out var set) || set.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var backends = new List<(int Key, string Destination)>();
            foreach (var entry in set.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 2)
                {
                    return false;
                }

                if (!TryGetInt32Value(entry[0], out var key) || !TryBuildBackendTarget(entry[1], out var destination))
                {
                    return false;
                }

                backends.Add((key, destination));
            }

            backendTargetMap = backends
                .OrderBy(backend => backend.Key)
                .ToArray();
            return backendTargetMap.Length > 0;
        }

        private static bool TryBuildBackendTarget(JsonElement expression, out string backendTarget)
        {
            backendTarget = string.Empty;

            if (!TryGetProperty(expression, "concat", out var concat) || concat.ValueKind != JsonValueKind.Array || concat.GetArrayLength() != 2)
            {
                return false;
            }

            if (!TryGetScalarText(concat[0], out var address) || !TryGetInt32Value(concat[1], out var port))
            {
                return false;
            }

            backendTarget = $"{address} . {port}";
            return true;
        }

        private static bool TryReadPayloadMatch(JsonElement statement, string expectedField, out string familyMatch, out string value)
        {
            familyMatch = string.Empty;
            value = string.Empty;

            if (!TryReadMatch(statement, out var left, out var right) || !TryGetProperty(left, "payload", out var payload))
            {
                return false;
            }

            if (!TryGetStringProperty(payload, "protocol", out familyMatch) || !TryGetStringProperty(payload, "field", out var field) || !string.Equals(field, expectedField, StringComparison.Ordinal))
            {
                return false;
            }

            return TryGetScalarText(right, out value);
        }

        private static bool TryReadCtMatch(JsonElement statement, string? expectedKey, out string familyMatch, out string value)
        {
            familyMatch = string.Empty;
            value = string.Empty;

            if (!TryReadMatch(statement, out var left, out var right) || !TryGetProperty(left, "ct", out var ct))
            {
                return false;
            }

            if (expectedKey != null && (!TryGetStringProperty(ct, "key", out var key) || !string.Equals(key, expectedKey, StringComparison.Ordinal)))
            {
                return false;
            }

            TryGetStringProperty(ct, "family", out familyMatch);
            return TryGetScalarText(right, out value);
        }

        private static bool TryReadMatch(JsonElement statement, out JsonElement left, out JsonElement right)
        {
            left = default;
            right = default;

            if (!TryGetProperty(statement, "match", out var match) || !TryGetProperty(match, "left", out left) || !TryGetProperty(match, "right", out right))
            {
                return false;
            }

            return true;
        }

        private static bool IsNamedStatement(JsonElement statement, string name)
        {
            return statement.ValueKind == JsonValueKind.Object && statement.EnumerateObject().Any(property => string.Equals(property.Name, name, StringComparison.Ordinal));
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out propertyValue))
            {
                return true;
            }

            propertyValue = default;
            return false;
        }

        private static bool TryGetArrayProperty(JsonElement element, string propertyName, out JsonElement[] values)
        {
            values = [];

            if (!TryGetProperty(element, propertyName, out var propertyValue) || propertyValue.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            values = propertyValue.EnumerateArray().ToArray();
            return true;
        }

        private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
        {
            value = string.Empty;

            if (!TryGetProperty(element, propertyName, out var propertyValue))
            {
                return false;
            }

            return TryGetScalarText(propertyValue, out value);
        }

        private static bool TryGetInt32Value(JsonElement element, out int value)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.TryGetInt32(out value);
            }

            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        private static bool TryGetScalarText(JsonElement element, out string value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    value = element.GetString() ?? string.Empty;
                    return true;
                case JsonValueKind.Number:
                    value = element.GetRawText();
                    return true;
                case JsonValueKind.True:
                    value = bool.TrueString.ToLowerInvariant();
                    return true;
                case JsonValueKind.False:
                    value = bool.FalseString.ToLowerInvariant();
                    return true;
                default:
                    value = string.Empty;
                    return false;
            }
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

        private static string BuildServiceRuleDefinition(BulbRule rule, TargetEndpoint[] backends)
        {
            var familyMatch = rule.IsIpv6 ? "ip6" : "ip";
            var protocolMatch = rule.IsTcp ? "tcp" : "udp";
            var backendTargets = string.Join(", ", backends.Select((backend, index) => $"{index} : {BuildBackendMapDestination(backend)}"));

            return $"prerouting {familyMatch} daddr {rule.LoadbalancerIp} {protocolMatch} dport {rule.LoadbalancerPort} dnat {familyMatch} to numgen inc mod {backends.Length} map {{ {backendTargets} }} comment \"{BulbComment}\"";
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

        private static string RunNft(string args)
        {
            return ShellUtils.RunCommand("nft", args);
        }
    }
}