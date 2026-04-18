using System.Net;
using Bulb.Contract;
using Bulb.Contract.Caching;
using Bulb.Models;
using k8s.Models;

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

        public IEnumerable<TargetEndpoint> ResolveEndpointsForServicePort(V1Service svc, V1Node myNode, V1ServicePort servicePort)
        {
            var targetPortValue = servicePort.TargetPort?.ToString();
            _logger.LogDebug(
                "Resolving endpoints for service {Namespace}/{ServiceName}, servicePort {ServicePortName}:{ServicePortNumber}, targetPort {TargetPort}, node {NodeName}.",
                svc.Namespace(),
                svc.Name(),
                servicePort.Name ?? "<unnamed>",
                servicePort.Port,
                targetPortValue ?? "<none>",
                myNode.Metadata.Name);

            var allEndpointSlices = _endpointSliceCache.Get(@namespace: svc.Namespace()).ToList();
            var endpointSlices = allEndpointSlices.Where(es =>
            {
                var hasServiceNameLabel = es.Metadata?.Labels != null
                    && es.Metadata.Labels.TryGetValue("kubernetes.io/service-name", out var serviceName)
                    && string.Equals(serviceName, svc.Name(), StringComparison.Ordinal);
                var hasServiceOwnerRef = es.OwnerReferences() != null
                    && es.OwnerReferences().Any(or => or.Kind == "Service" && or.Name == svc.Name());
                return hasServiceNameLabel || hasServiceOwnerRef;
            });

            _logger.LogDebug(
                "Found {TotalSliceCount} EndpointSlices in namespace {Namespace}; {ServiceSliceCount} belong to service {ServiceName}.",
                allEndpointSlices.Count,
                svc.Namespace(),
                endpointSlices.Count(),
                svc.Name());

            var servicePortName = servicePort.Name;
            var targetPortName = int.TryParse(targetPortValue, out _) ? null : targetPortValue;
            var hasTargetPortNumber = int.TryParse(targetPortValue, out var targetPortNumber);

            var isServiceLocal = string.Equals(svc.Spec.ExternalTrafficPolicy, "Local", StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("Service {ServiceName} uses ExternalTrafficPolicy Local={IsLocal}.", svc.Name(), isServiceLocal);

            List<TargetEndpoint> endpoints = [];
            HashSet<string> seenEndpoints = new(StringComparer.Ordinal);

            foreach (var endpointSlice in endpointSlices)
            {
                foreach (var port in endpointSlice.Ports ?? Enumerable.Empty<Discoveryv1EndpointPort>())
                {
                    if (port.Port == null)
                    {
                        continue;
                    }

                    if (!IsMatchingSlicePort(port, servicePortName, targetPortName, hasTargetPortNumber, targetPortNumber, servicePort.Port))
                    {
                        continue;
                    }

                    var resolved = ResolveEndpointsForPort(endpointSlice.Endpoints ?? Enumerable.Empty<V1Endpoint>(), port, isServiceLocal, myNode.Metadata.Name)
                        .Where(ep => seenEndpoints.Add($"{ep.Address}:{ep.TargetPort}"))
                        .ToList();
                    endpoints.AddRange(resolved);

                    _logger.LogDebug(
                        "Matched EndpointSlice port {PortName}:{PortNumber} in {EndpointSliceName}; resolved {ResolvedCount} endpoints.",
                        port.Name ?? "<unnamed>",
                        port.Port.Value,
                        endpointSlice.Name(),
                        resolved.Count);
                }
            }

            _logger.LogInformation(
                "Resolved {EndpointCount} endpoints for service {Namespace}/{ServiceName}, servicePort {ServicePortName}:{ServicePortNumber}, targetPort {TargetPort}.",
                endpoints.Count,
                svc.Namespace(),
                svc.Name(),
                servicePort.Name ?? "<unnamed>",
                servicePort.Port,
                targetPortValue ?? "<none>");

            return endpoints;
        }

        private static bool IsMatchingSlicePort(
            Discoveryv1EndpointPort slicePort,
            string? servicePortName,
            string? targetPortName,
            bool hasTargetPortNumber,
            int targetPortNumber,
            int servicePortNumber)
        {
            if (hasTargetPortNumber)
            {
                return slicePort.Port == targetPortNumber;
            }

            if (!string.IsNullOrWhiteSpace(servicePortName) && string.Equals(slicePort.Name, servicePortName, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(targetPortName) && string.Equals(slicePort.Name, targetPortName, StringComparison.Ordinal))
            {
                return true;
            }

            // When targetPort is omitted, Kubernetes defaults it to the service port.
            return slicePort.Port == servicePortNumber;
        }

        private static IEnumerable<TargetEndpoint> ResolveEndpointsForPort(IEnumerable<V1Endpoint> endpoints, Discoveryv1EndpointPort port, bool isServiceLocal, string myNodeName)
        {
            foreach (var endpoint in endpoints)
            {
                if (endpoint.Conditions == null || endpoint.Conditions.Ready != true || endpoint.Conditions.Serving != true || endpoint.Conditions.Terminating == true)
                {
                    continue;
                }

                var isEndpointLocal = endpoint.NodeName != null && endpoint.NodeName.Equals(myNodeName, StringComparison.Ordinal);
                if (isServiceLocal && !isEndpointLocal)
                {
                    continue;
                }

                foreach (var ip in endpoint.Addresses)
                {
                    var ipAddress = IPAddress.Parse(ip);
                    yield return new TargetEndpoint(ipAddress, (short)port.Port!.Value, isEndpointLocal);
                }
            }
        }
    }
}
