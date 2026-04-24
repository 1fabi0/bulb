using Bulb.Contract;
using Bulb.Models;
using Bulb.Util;

namespace Bulb.Services.Listener
{
    public class NftablesBackendService : ILoadBalancerBackendService
    {
        public Task ApplyRulesAsync(IEnumerable<BulbRule> rules, IEnumerable<ScopeNodeIp> nodeInfos, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nodeIps = nodeInfos.Select(nodeInfo => nodeInfo.Address);
            var desiredRules = rules.Where(rule => nodeIps.Contains(rule.LoadbalancerIp));

            NftablesUtil.EnsureTableAndChains();

            var desiredRuleDefinitions = NftablesUtil.BuildDesiredRuleDefinitions(desiredRules);
            var existingRuleDefinitions = NftablesUtil.GetExistingManagedRuleDefinitions();

            var desiredCounts = BuildCountMap(desiredRuleDefinitions);
            var existingCounts = BuildCountMap(existingRuleDefinitions);

            foreach (var (ruleDefinition, existingCount) in existingCounts)
            {
                var desiredCount = desiredCounts.TryGetValue(ruleDefinition, out var value) ? value : 0;
                var deleteCount = existingCount - desiredCount;
                for (var index = 0; index < deleteCount; index++)
                {
                    NftablesUtil.DeleteRule(ruleDefinition);
                }
            }

            foreach (var (ruleDefinition, desiredCount) in desiredCounts)
            {
                var existingCount = existingCounts.TryGetValue(ruleDefinition, out var value) ? value : 0;
                var addCount = desiredCount - existingCount;
                for (var index = 0; index < addCount; index++)
                {
                    NftablesUtil.AddRule(ruleDefinition);
                }
            }

            return Task.CompletedTask;
        }

        private static Dictionary<string, int> BuildCountMap(IEnumerable<string> definitions)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                counts[definition] = counts.TryGetValue(definition, out var count) ? count + 1 : 1;
            }

            return counts;
        }
    }
}
