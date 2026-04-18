using System.Net;
using Bulb.Configuration;
using Bulb.Contract;
using Bulb.Contract.Caching;
using Bulb.Models;
using k8s.Models;

namespace Bulb.Services.Listener
{
    public class ListenerReconciler : IBulbReconciler
    {
        private readonly IServiceEndpointResolver _serviceEndpointResolver;
        private readonly ILoadBalancerBackendService _loadBalancerBackendService;
        private readonly ICache<V1Node> _nodeCache;
        private readonly ICache<V1Service> _serviceCache;
        private readonly BulbConfiguration _config;
        private readonly ILogger _logger;

        public ListenerReconciler(IServiceEndpointResolver serviceEndpointResolver,
            ILoadBalancerBackendService loadBalancerBackendService,
            ICache<V1Node> nodeCache,
            ICache<V1Service> serviceCache,
            BulbConfiguration config,
            ILogger<ListenerReconciler> logger)
        {
            _serviceEndpointResolver = serviceEndpointResolver;
            _loadBalancerBackendService = loadBalancerBackendService;
            _nodeCache = nodeCache;
            _serviceCache = serviceCache;
            _config = config;
            _logger = logger;
        }

        private V1Node? GetMyNode()
        {
            return _nodeCache.Get(name: _config.NodeName, @namespace: "default");
        }

        private static IReadOnlyCollection<string> SplitScopes(string? scopeValue)
        {
            return string.IsNullOrWhiteSpace(scopeValue)
                ? Array.Empty<string>()
                : scopeValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public async Task ReconcileAsync()
        {
            var myNode = GetMyNode();
            if (myNode == null)
            {
                _logger.LogWarning("My node {NodeName} not found in cache, skipping reconciliation.", _config.NodeName);
                return;
            }

            var services = _serviceCache.Get().ToList();
            _logger.LogInformation("Reconciling listeners for {ServiceCount} services.", services.Count);
            services = services.Where(svc => svc.Spec.Type == "LoadBalancer").ToList();
            _logger.LogInformation("{LoadBalancerServiceCount} services are of type LoadBalancer.", services.Count);

            const string bindingAnnotationPrefix = "bulb.io/bind-";
            IEnumerable<ScopeNodeIp> bindingIps = myNode.Metadata.Annotations
            .Where(kv => kv.Key.StartsWith(bindingAnnotationPrefix))
            .SelectMany(kv => kv.Value.Split(',')
                .Where(v => IPAddress.TryParse(v.Trim(), out _))
                .Select(v => new ScopeNodeIp(kv.Key.Substring(bindingAnnotationPrefix.Length), IPAddress.Parse(v.Trim()))));

            var bulbRules = new List<BulbRule>();

            foreach (var service in services)
            {
                var bulbScope = service.Metadata.Annotations.FirstOrDefault(kv => kv.Key == "bulb.io/scope").Value;
                var bulbEndpointRoutingValue = service.Metadata.Annotations.FirstOrDefault(kv => kv.Key == "bulb.io/endpoint-routing-enabled").Value;
                var bulbEndpointRoutingEnabled = bool.TryParse(bulbEndpointRoutingValue, out var parsedValue) ? parsedValue : _config.IsEndpointRoutingEnabled;
                var serviceScopes = SplitScopes(bulbScope ?? _config.DefaultScope);
                if(serviceScopes.Count == 0)
                {
                    _logger.LogInformation("Service {Namespace}/{ServiceName} has no bulb scope annotation and no default scope is configured. Skipping.", service.Namespace(), service.Name());
                    // skip if no scope defined
                    continue;
                }
                
                var scopeIps = bindingIps.Where(bi => serviceScopes.Contains(bi.Scope)).ToList();
                if(scopeIps.Count == 0)
                {
                    _logger.LogInformation("No binding IP found for service {Namespace}/{ServiceName} with scopes {Scope}. Skipping.", service.Namespace(), service.Name(), string.Join(", ", serviceScopes));
                    continue;
                }

                foreach (var servicePort in service.Spec.Ports)
                {
                    IEnumerable<TargetEndpoint> endpoints;
                    if (!bulbEndpointRoutingEnabled)
                    {
                        endpoints = service.Spec.ClusterIPs.Select(cip => new TargetEndpoint(IPAddress.Parse(cip), (short)servicePort.Port));
                    }
                    else
                    {
                        endpoints = _serviceEndpointResolver.ResolveEndpointsForServicePort(service, myNode, servicePort);
                    }

                    var resolvedEndpoints = endpoints.ToList();
                    _logger.LogInformation("Resolved {EndpointCount} endpoints for service {Namespace}/{ServiceName} port {Port}.", resolvedEndpoints.Count, service.Namespace(), service.Name(), servicePort.Port);

                    foreach (var scopeIp in scopeIps)
                    {
                        var familyEndpoints = resolvedEndpoints
                            .Where(ep => ep.IsIpv6 == scopeIp.Address.AddressFamily.Equals(System.Net.Sockets.AddressFamily.InterNetworkV6))
                            .ToList();
                        if (familyEndpoints.Count == 0)
                        {
                            _logger.LogInformation("No endpoints with matching address family found for service {Namespace}/{ServiceName} port {Port} and VIP {Vip}. Skipping rule.", service.Namespace(), service.Name(), servicePort.Port, scopeIp.Address);
                            continue;
                        }

                        var bulbRule = new BulbRule(
                                backends: familyEndpoints,
                                loadbalancerIp: scopeIp.Address,
                                loadbalancerPort: (short)servicePort.Port,
                                protocol: servicePort.Protocol);
                        bulbRules.Add(bulbRule);
                    }
                }
            }

            _logger.LogInformation("Applying {RuleCount} bulb rules to load balancer.", bulbRules.Count);
            await _loadBalancerBackendService.ApplyRulesAsync(bulbRules, bindingIps, CancellationToken.None);
        }
    }
}
