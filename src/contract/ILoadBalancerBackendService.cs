using Bulb.Models;

namespace Bulb.Contract
{
    public interface ILoadBalancerBackendService
    {
        Task ApplyRulesAsync(IEnumerable<BulbRule> rules, IEnumerable<ScopeNodeIp> nodeInfos, CancellationToken cancellationToken);
    }
}