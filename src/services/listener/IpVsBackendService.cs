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
            var existingRules = GetExistingRules(nodeIps, cancellationToken).ToArray();
            // todo: also use iptables source natting if target endpoint is not local
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
                        }
                    }
                    var backendsToDelete = existingRule.Backends.Where(backend => !rule.Backends.Any(b => b.Address.Equals(backend.Address) && b.TargetPort == backend.TargetPort));
                    foreach (var backendToDelete in backendsToDelete)
                    {
                        // Backend exists but not in the new rule, remove it
                        string serviceAddress = BuildServiceAddress(rule.LoadbalancerIp, rule.LoadbalancerPort, rule.IsIpv6);
                        string backendAddress = BuildServiceAddress(backendToDelete.Address, backendToDelete.TargetPort, backendToDelete.IsIpv6);
                        IpVsUtil.RunIpvsAdm($"-d -{protocolOption} {serviceAddress} -r {backendAddress}");
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
            }
            return Task.CompletedTask;
        }

        private static IEnumerable<BulbRule> GetExistingRules(IEnumerable<ScopeNodeIp> nodeIps, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = IpVsUtil.RunIpvsAdm("-Ln --exact");
            var rules = IpVsUtil.ParseIpVsAdmOutput(output);

            var iptablesOutput = IpTablesUtil.RunIpTables("-t nat -L -n");
            var ip6tablesOutput = IpTablesUtil.RunIpTables("-t nat -L -n", isIpv6: true);
            IpTablesUtil.ParseNatTableOutput(iptablesOutput + "\n" + ip6tablesOutput, rules);

            return rules.Where(rule => nodeIps.Any(nodeIp => nodeIp.Address.Equals(rule.LoadbalancerIp.ToString())));
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