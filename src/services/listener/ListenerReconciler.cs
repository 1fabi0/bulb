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
            ILogger logger)
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

            IEnumerable<ScopeNodeIp> bindingIps = myNode.Metadata.Annotations.Where(kv => kv.Key.StartsWith("bulb.io/bind-") && IPAddress.TryParse(kv.Value, out _))
                .Select(kv => new ScopeNodeIp(kv.Key.TrimStart("bulb.io/bind-").ToString(), IPAddress.Parse(kv.Value)));

            var bulbRules = new List<BulbRule>();

            foreach (var service in services)
            {
                var bulbScope = service.Metadata.Annotations.FirstOrDefault(kv => kv.Key == "bulb.io/scope").Value;
                var bulbEndpointRoutingValue = service.Metadata.Annotations.FirstOrDefault(kv => kv.Key == "bulb.io/endpoint-routing-enabled").Value;
                var bulbEndpointRoutingEnabled = bool.TryParse(bulbEndpointRoutingValue, out var parsedValue) ? parsedValue : _config.IsEndpointRoutingEnabled;
                if(bulbScope == null && _config.DefaultScope == null)
                {
                    _logger.LogInformation("Service {Namespace}/{ServiceName} has no bulb scope annotation and no default scope is configured. Skipping.", service.Namespace(), service.Name());
                    // skip if no scope defined
                    continue;
                }
                
                var scopeIp = bindingIps.FirstOrDefault(bi => bi.Scope == (bulbScope ?? _config.DefaultScope));
                if(scopeIp == null)
                {
                    _logger.LogInformation("No binding IP found for service {Namespace}/{ServiceName} with scope {Scope}. Skipping.", service.Namespace(), service.Name(), bulbScope ?? _config.DefaultScope);
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
                        endpoints = _serviceEndpointResolver.ResolveEndpointsForPortAsync(service, myNode, servicePort.TargetPort);
                    }

                    _logger.LogInformation("Resolved {EndpointCount} endpoints for service {Namespace}/{ServiceName} port {Port}.", endpoints.Count(), service.Namespace(), service.Name(), servicePort.Port);
                    
                    var bulbRule = new BulbRule(
                            backends: endpoints,
                            loadbalancerIp: scopeIp.Address,
                            loadbalancerPort: (short)servicePort.Port,
                            protocol: servicePort.Protocol);
                    bulbRules.Add(bulbRule);
                }
            }

            _logger.LogInformation("Applying {RuleCount} bulb rules to load balancer.", bulbRules.Count);
            await _loadBalancerBackendService.ApplyRulesAsync(bulbRules, bindingIps, CancellationToken.None);
        }
    }
}
