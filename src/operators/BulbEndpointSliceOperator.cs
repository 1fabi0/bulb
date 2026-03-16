using Bulb.Contract;
using Bulb.Contract.Caching;
using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;

namespace Bulb.Operators
{
    [EntityRbac(typeof(V1EndpointSlice), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    public class BulbEndpointSliceOperator : IEntityController<V1EndpointSlice>
    {
        private readonly ICache<V1EndpointSlice> _endpointSliceCache;
        private readonly IBulbReconciler _bulbReconciler;
        private readonly ILogger _logger;
        
        public BulbEndpointSliceOperator(ICache<V1EndpointSlice> cache, ILogger<BulbEndpointSliceOperator> logger, IBulbReconciler bulbReconciler)
        {
            _endpointSliceCache = cache;
            _bulbReconciler = bulbReconciler;
            _logger = logger;
        }

        public async Task<ReconciliationResult<V1EndpointSlice>> DeletedAsync(V1EndpointSlice entity, CancellationToken cancellationToken)
        {
            try
            {
                _endpointSliceCache.Remove(entity);
                await _bulbReconciler.ReconcileAsync();
                _logger.LogInformation($"Endpoint slice {entity.Metadata.Name} removed from cache and bulb reconciled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing endpoint slice {entity.Metadata.Name} from cache");
                return ReconciliationResult<V1EndpointSlice>.Failure(entity, errorMessage: "Error removing endpoint slice from bulb", error: ex, requeueAfter: TimeSpan.FromSeconds(30));
            }
            return ReconciliationResult<V1EndpointSlice>.Success(entity);
        }

        public async Task<ReconciliationResult<V1EndpointSlice>> ReconcileAsync(V1EndpointSlice entity, CancellationToken cancellationToken)
        {
            try
            {
                _endpointSliceCache.AddOrUpdate(entity);
                 await _bulbReconciler.ReconcileAsync();
                _logger.LogInformation($"Endpoint slice {entity.Metadata.Name} added/updated in cache and bulb reconciled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding/updating endpoint slice {entity.Metadata.Name} in cache");
                return ReconciliationResult<V1EndpointSlice>.Failure(entity, errorMessage: "Error adding/updating endpoint slice in bulb", error: ex, requeueAfter: TimeSpan.FromSeconds(30));
            }
            return ReconciliationResult<V1EndpointSlice>.Success(entity);
        }
    }
}
