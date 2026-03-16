using System.Net;
using Bulb.Contract;
using Bulb.Contract.Caching;
using Bulb.Models;
using k8s.Models;
using KubeOps.KubernetesClient;

namespace Bulb.Services.Listener
{
    public class ServiceEndpointResolver : IServiceEndpointResolver
    {
        private readonly ICache<V1EndpointSlice> _endpointSliceCache;
        private readonly ILogger<ServiceEndpointResolver> _logger;

        public ServiceEndpointResolver(ICache<V1EndpointSlice> endpointSliceCache, ILogger<ServiceEndpointResolver> logger)
        {
            _endpointSliceCache = endpointSliceCache;
            _logger = logger;
        }

        public IEnumerable<TargetEndpoint> ResolveEndpointsForPortAsync(V1Service svc, V1Node myNode, IntOrString serviceTargetPort)
        {
            _logger.LogDebug(
                "Resolving endpoints for service {Namespace}/{ServiceName}, targetPort {TargetPort}, node {NodeName}.",
                svc.Namespace(),
                svc.Name(),
                serviceTargetPort.ToString(),
                myNode.Metadata.Name);

            var allEndpointSlices = _endpointSliceCache.Get(@namespace: svc.Namespace()).ToList();
            var endpointSlices = allEndpointSlices.Where(es => es.OwnerReferences() != null
                && es.OwnerReferences().Any(or => or.Kind == "Service" && or.Name == svc.Name()));

            _logger.LogDebug(
                "Found {TotalSliceCount} EndpointSlices in namespace {Namespace}; {ServiceSliceCount} belong to service {ServiceName}.",
                allEndpointSlices.Count,
                svc.Namespace(),
                endpointSlices.Count(),
                svc.Name());

            bool isServicePortInt = int.TryParse(serviceTargetPort.Value, out var serviceTargetPortInt);
            bool isServiceLocal = svc.Spec.ExternalTrafficPolicy.Equals("Local", StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("Service {ServiceName} uses ExternalTrafficPolicy Local={IsLocal}.", svc.Name(), isServiceLocal);

            List<TargetEndpoint> endpoints = [];

            foreach (var endpointSlice in endpointSlices)
            {
                foreach (var port in endpointSlice.Ports)
                {
                    if (port.Port == null)
                    {
                        continue;
                    }

                    if (port.Name != null && !isServicePortInt)
                    {
                        // try to match by name first
                        if (port.Name.Equals(serviceTargetPort.ToString(), StringComparison.Ordinal))
                        {
                            var resolved = ResolveEndpointsForPort(endpointSlice.Endpoints, port, isServiceLocal, myNode.Metadata.Name).ToList();
                            endpoints.AddRange(resolved);
                            _logger.LogDebug(
                                "Matched port by name {PortName} in EndpointSlice {EndpointSliceName}; resolved {ResolvedCount} endpoints.",
                                port.Name,
                                endpointSlice.Name(),
                                resolved.Count);
                        }
                    }
                    else
                    {
                        // fallback to matching by port number
                        if (port.Port.Value == serviceTargetPortInt)
                        {
                            var resolved = ResolveEndpointsForPort(endpointSlice.Endpoints, port, isServiceLocal, myNode.Metadata.Name).ToList();
                            endpoints.AddRange(resolved);
                            _logger.LogDebug(
                                "Matched port by number {PortNumber} in EndpointSlice {EndpointSliceName}; resolved {ResolvedCount} endpoints.",
                                port.Port.Value,
                                endpointSlice.Name(),
                                resolved.Count);
                        }
                    }
                }
            }

            _logger.LogInformation(
                "Resolved {EndpointCount} endpoints for service {Namespace}/{ServiceName}, targetPort {TargetPort}.",
                endpoints.Count,
                svc.Namespace(),
                svc.Name(),
                serviceTargetPort.ToString());

            return endpoints;
        }
        private static IEnumerable<TargetEndpoint> ResolveEndpointsForPort(IEnumerable<V1Endpoint> endpoints, Discoveryv1EndpointPort port, bool isServiceLocal, string myNodeName)
        {
            foreach (var endpoint in endpoints)
            {
                if (endpoint.Conditions.Ready != true || endpoint.Conditions.Serving != true || endpoint.Conditions.Terminating == true)
                {
                    continue;
                }
                if (isServiceLocal)
                {
                    // if the service is local only, filter endpoints for the ones that are on the same node
                    if (endpoint.NodeName == null || !endpoint.NodeName.Equals(myNodeName, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                foreach (var ip in endpoint.Addresses)
                {
                    var ipAdress = IPAddress.Parse(ip);
                    yield return new TargetEndpoint(ipAdress, (short)port.Port!.Value, isServiceLocal); // port.Port is guaranteed to have a value here since we only call this method for ports that have a port number
                }
            }
        }
    }
}
