using System.Net;
using Bulb.Contract;
using Bulb.Models;
using Bulb.Util;

namespace Bulb.Services.Listener
{
    public class IpVsBackendService : ILoadBalancerBackendService
    {
        public Task ApplyRulesAsync(IEnumerable<BulbRule> rules, IEnumerable<ScopeNodeIp> nodeIps, CancellationToken cancellationToken)
        {
            NftablesUtil.EnsureTableAndChains();

            var existingRules = GetExistingRules(nodeIps, cancellationToken).ToArray();

            foreach(var rule in rules)
            {
                var existingRule = existingRules.FirstOrDefault(r => r.LoadbalancerIp.Equals(rule.LoadbalancerIp) && r.LoadbalancerPort == rule.LoadbalancerPort && r.IsTcp == rule.IsTcp);
                string protocolOption = GetProtocolOption(rule);
                if (existingRule == null)
                {
                    // Rule doesn't exist, add it
                    string serviceAddress = BuildServiceAddress(rule.LoadbalancerIp, rule.LoadbalancerPort, rule.IsIpv6);

                    IpVsUtil.RunIpvsAdm($"-A -{protocolOption} {serviceAddress} -s rr");
                    foreach (var backend in rule.Backends)
                    {
                        if(backend.IsIpv6 != rule.IsIpv6)
                        {
                            throw new InvalidOperationException("Backend and service IP versions do not match.");
                        }
                        string backendAddress = BuildServiceAddress(backend.Address, backend.TargetPort, backend.IsIpv6);
                        IpVsUtil.RunIpvsAdm($"-a -{protocolOption} {serviceAddress} -r {backendAddress} -m");
                        if(!backend.IsLocal)
                        {
                            NftablesUtil.AddRule(NftablesUtil.BuildMasqueradeRuleDefinition(rule.LoadbalancerIp, rule.LoadbalancerPort, backend.Address, backend.TargetPort, rule.IsTcp));
                        }
                    }
                }
                else
                {
                    // Rule exists, check if backends match
                    foreach (var backend in rule.Backends){
                        if(backend.IsIpv6 != rule.IsIpv6)
                        {
                            throw new InvalidOperationException("Backend and service IP versions do not match.");
                        }
                        var existingBackend = existingRule.Backends.FirstOrDefault(b => b.Address.Equals(backend.Address) && b.TargetPort == backend.TargetPort);
                        if (existingBackend == null)
                        {
                            // Backend doesn't exist, add it
                            string serviceAddress = BuildServiceAddress(rule.LoadbalancerIp, rule.LoadbalancerPort, rule.IsIpv6);
                            string backendAddress = BuildServiceAddress(backend.Address, backend.TargetPort, backend.IsIpv6);
                            IpVsUtil.RunIpvsAdm($"-a -{protocolOption} {serviceAddress} -r {backendAddress} -m");
                            if(!backend.IsLocal)
                            {
                                NftablesUtil.AddRule(NftablesUtil.BuildMasqueradeRuleDefinition(rule.LoadbalancerIp, rule.LoadbalancerPort, backend.Address, backend.TargetPort, rule.IsTcp));
                            }
                        }
                        else if(existingBackend.IsLocal != backend.IsLocal)
                        {
                            // Backend exists but local flag has changed, update it
                            if(backend.IsLocal)
                            {
                                // Local flag changed to true, remove SNAT rule
                                NftablesUtil.DeleteRule(NftablesUtil.BuildMasqueradeRuleDefinition(rule.LoadbalancerIp, rule.LoadbalancerPort, backend.Address, backend.TargetPort, rule.IsTcp));
                            }
                            else
                            {
                                // Local flag changed to false, add SNAT rule
                                NftablesUtil.AddRule(NftablesUtil.BuildMasqueradeRuleDefinition(rule.LoadbalancerIp, rule.LoadbalancerPort, backend.Address, backend.TargetPort, rule.IsTcp));
                            }
                        }
                    }
                    var backendsToDelete = existingRule.Backends.Where(backend => !rule.Backends.Any(b => b.Address.Equals(backend.Address) && b.TargetPort == backend.TargetPort));
                    foreach (var backendToDelete in backendsToDelete)
                    {
                        // Backend exists but not in the new rule, remove it
                        string serviceAddress = BuildServiceAddress(rule.LoadbalancerIp, rule.LoadbalancerPort, rule.IsIpv6);
                        string backendAddress = BuildServiceAddress(backendToDelete.Address, backendToDelete.TargetPort, backendToDelete.IsIpv6);
                        IpVsUtil.RunIpvsAdm($"-d -{protocolOption} {serviceAddress} -r {backendAddress}");
                        if(!backendToDelete.IsLocal)
                        {
                            NftablesUtil.DeleteRule(NftablesUtil.BuildMasqueradeRuleDefinition(rule.LoadbalancerIp, rule.LoadbalancerPort, backendToDelete.Address, backendToDelete.TargetPort, rule.IsTcp));
                        }
                    }
                }
            }

            var rulesToDelete = existingRules.Where(existingRule => !rules.Any(r => r.LoadbalancerIp.Equals(existingRule.LoadbalancerIp) && r.LoadbalancerPort == existingRule.LoadbalancerPort && r.IsTcp == existingRule.IsTcp));
            foreach (var existingRule in rulesToDelete)
            {
                // Rule exists but not in the new rules, remove it
                string protocolOption = GetProtocolOption(existingRule);
                string serviceAddress = BuildServiceAddress(existingRule.LoadbalancerIp, existingRule.LoadbalancerPort, existingRule.IsIpv6);
                IpVsUtil.RunIpvsAdm($"-D -{protocolOption} {serviceAddress}");
                foreach (var backend in existingRule.Backends)
                {
                    if(!backend.IsLocal)
                    {
                        NftablesUtil.DeleteRule(NftablesUtil.BuildMasqueradeRuleDefinition(existingRule.LoadbalancerIp, existingRule.LoadbalancerPort, backend.Address, backend.TargetPort, existingRule.IsTcp));
                    }
                }
            }
            return Task.CompletedTask;
        }

        private static IEnumerable<BulbRule> GetExistingRules(IEnumerable<ScopeNodeIp> nodeIps, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = IpVsUtil.RunIpvsAdm("-Ln --exact");
            var rules = IpVsUtil.ParseIpVsAdmOutput(output);

            var nftRules = NftablesUtil.GetExistingManagedRuleDefinitions().ToList();
            foreach (var rule in rules)
            {
                foreach (var backend in rule.Backends)
                {
                    var snatRule = NftablesUtil.BuildMasqueradeRuleDefinition(rule.LoadbalancerIp, rule.LoadbalancerPort, backend.Address, backend.TargetPort, rule.IsTcp);
                    if (nftRules.Contains(snatRule))
                    {
                        backend.IsLocal = false;
                    }
                }
            }

            return rules.Where(rule => nodeIps.Any(nodeIp => nodeIp.Address.Equals(rule.LoadbalancerIp)));
        }

        private static string GetProtocolOption(BulbRule rule)
        {
            if (rule.IsTcp)
            {
                return "t";
            }

            if (rule.IsUdp)
            {
                return "u";
            }

            throw new InvalidOperationException("Only TCP and UDP protocols are supported.");
        }

        private static string BuildServiceAddress(IPAddress address, short port, bool isIpv6)
        {
            return isIpv6 ? $"[{address}]:{port}" : $"{address}:{port}";
        } 
    }
}