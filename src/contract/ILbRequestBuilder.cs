using Bulb.Models;
using k8s.Models;

namespace Bulb.Contract
{
    public interface ILbRequestBuilder
    {
        Task<LoadBalanceRequest> BuildRequestAsync(V1Service svc, V1Node node);
    }
}