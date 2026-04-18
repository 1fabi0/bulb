using Bulb.Models;
using k8s.Models;

namespace Bulb.Contract
{
    public interface IServiceEndpointResolver
    {
        IEnumerable<TargetEndpoint> ResolveEndpointsForServicePort(V1Service svc, V1Node myNode, V1ServicePort servicePort);
    }
}