using System.Net;
using Bulb.Configuration;
using Bulb.Contract;
using Bulb.Contract.Caching;
using Bulb.Models;
using k8s.Models;
using KubeOps.KubernetesClient;

namespace Bulb.Services.Status
{
    public class StatusReconciler : IBulbReconciler
    {
        private readonly ICache<V1Node> _nodeCache;
        private readonly ICache<V1Service> _serviceCache;
        private readonly IKubernetesClient _kubernetesClient;
        private readonly ILogger _logger;
        private readonly BulbConfiguration _config;

        public StatusReconciler(ICache<V1Node> nodeCache, ICache<V1Service> serviceCache, IKubernetesClient kubernetesClient, ILogger logger, BulbConfiguration config)
        {
            _nodeCache = nodeCache;
            _serviceCache = serviceCache;
            _kubernetesClient = kubernetesClient;
            _logger = logger;
            _config = config;
        }

        public async Task ReconcileAsync()
        {
            var services = _serviceCache.Get().ToList();
            _logger.LogInformation("Reconciling listeners for {ServiceCount} services.", services.Count);
            services = services.Where(svc => svc.Spec.Type == "LoadBalancer").ToList();
            _logger.LogInformation("{LoadBalancerServiceCount} services are of type LoadBalancer.", services.Count);

            IEnumerable<ScopeNodeIp> bindingIps = _nodeCache.Get().SelectMany(n => n.Metadata.Annotations.Where(kv => kv.Key.StartsWith("bulb.io/bind-") && IPAddress.TryParse(kv.Value, out _))
                .Select(kv => new ScopeNodeIp(kv.Key.TrimStart("bulb.io/bind-").ToString(), IPAddress.Parse(kv.Value))));

            IEnumerable<ScopeNodeIp> displayIps = _nodeCache.Get().SelectMany(n => n.Metadata.Annotations.Where(kv => kv.Key.StartsWith("bulb.io/display-") && IPAddress.TryParse(kv.Value, out _))
                .Select(kv => new ScopeNodeIp(kv.Key.TrimStart("bulb.io/display-").ToString(), IPAddress.Parse(kv.Value))));

            IEnumerable<ScopeNodeIp> finalDisplayIps = bindingIps.Select(bi => 
                { 
                    var displayIp = displayIps.FirstOrDefault(di => di.Scope == bi.Scope);
                    if(displayIp == null)
                    { 
                        return bi; 
                    } 
                    return displayIp;
                });

            foreach(var service in services)
            {
                var bulbScope = service.Metadata.Annotations.FirstOrDefault(kv => kv.Key == "bulb.io/scope").Value;
                if(bulbScope == null && _config.DefaultScope == null)
                {
                    _logger.LogInformation("Service {Namespace}/{ServiceName} has no bulb scope annotation and no default scope is configured. Skipping.", service.Namespace(), service.Name());
                    // skip if no scope defined
                    continue;
                }
                
                var scopeIps = finalDisplayIps.Where(bi => bi.Scope == (bulbScope ?? _config.DefaultScope));
                if(!scopeIps.Any())
                {
                    _logger.LogInformation("No binding IP found for service {Namespace}/{ServiceName} with scope {Scope}. Skipping.", service.Namespace(), service.Name(), bulbScope ?? _config.DefaultScope);
                    continue;
                }

                var serviceStatus = new V1ServiceStatus
                {
                    LoadBalancer = new V1LoadBalancerStatus
                    {
                        Ingress = [.. scopeIps.Select(si => new V1LoadBalancerIngress 
                            { 
                                Ip = si.Address.ToString(),
                                Ports = [.. service.Spec.Ports.Select(p => new V1PortStatus 
                                { 
                                    Port = p.Port,
                                    Protocol = p.Protocol 
                                })],
                                IpMode = "Proxy"
                            })], 
                    }
                };

                if(service.Status == null || !StatusEqual(serviceStatus, service.Status))
                {
                    service.Status = serviceStatus;
                    await _kubernetesClient.UpdateStatusAsync(service);
                    _logger.LogInformation("Updating status for service {Namespace}/{ServiceName} with {IngressCount} ingress entries.", service.Namespace(), service.Name(), serviceStatus.LoadBalancer.Ingress.Count);
                }
                else
                {
                    _logger.LogInformation("Status for service {Namespace}/{ServiceName} is up to date. No update needed.", service.Namespace(), service.Name());
                }
            }
        }

        private static bool StatusEqual(V1ServiceStatus s1, V1ServiceStatus s2)
        {
            if(s1.LoadBalancer == null && s2.LoadBalancer == null)
            {
                return true;
            }
            if(s1.LoadBalancer == null || s2.LoadBalancer == null)
            {
                return false;
            }
            if(s1.LoadBalancer.Ingress.Count != s2.LoadBalancer.Ingress.Count)
            {
                return false;
            }

            for(int i = 0; i < s1.LoadBalancer.Ingress.Count; i++)
            {
                var i1 = s1.LoadBalancer.Ingress[i];
                var i2 = s2.LoadBalancer.Ingress[i];
                if(i1.Ip != i2.Ip || i1.Hostname != i2.Hostname || i1.IpMode != i2.IpMode)
                {
                    return false;
                }
                if ((i1.Ports == null) != (i2.Ports == null))
                {
                    return false;
                }
                if (i1.Ports != null && i2.Ports != null)
                {
                    if (i1.Ports.Count != i2.Ports.Count)
                    {
                        return false;
                    }
                    for (int jp = 0; jp < i1.Ports.Count; jp++)
                    {
                        var p1 = i1.Ports[jp];
                        var p2 = i2.Ports[jp];
                        if (p1.Port != p2.Port || p1.Protocol != p2.Protocol)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }
}
