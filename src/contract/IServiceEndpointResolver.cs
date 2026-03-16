using Bulb.Models;
using k8s.Models;

namespace Bulb.Contract
{
    public interface IServiceEndpointResolver
    {
        IEnumerable<TargetEndpoint> ResolveEndpointsForPortAsync(V1Service svc, V1Node myNode, IntOrString servicePort);
    }
}