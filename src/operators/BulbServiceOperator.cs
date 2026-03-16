using Bulb.Contract.Caching;
using Bulb.Contract;
using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;

namespace Bulb.Operators
{
    [EntityRbac(typeof(V1Service), Verbs = RbacVerb.List | RbacVerb.Watch | RbacVerb.Get | RbacVerb.Update | RbacVerb.Patch)]
    public class BulbServiceOperator : IEntityController<V1Service>
    {
        private readonly ICache<V1Service> _serviceCache;
        private readonly IBulbReconciler _bulbReconciler;
        private readonly ILogger _logger;
        
        public BulbServiceOperator(ICache<V1Service> cache, ILogger<BulbServiceOperator> logger, IBulbReconciler bulbReconciler)
        {
            _serviceCache = cache;
            _bulbReconciler = bulbReconciler;
            _logger = logger;
        }

        public async Task<ReconciliationResult<V1Service>> DeletedAsync(V1Service entity, CancellationToken cancellationToken)
        {
            try
            {
                _serviceCache.Remove(entity);
                await _bulbReconciler.ReconcileAsync();
                _logger.LogInformation($"Service {entity.Metadata.Name} removed from cache and bulb reconciled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing service {entity.Metadata.Name} from cache");
                return ReconciliationResult<V1Service>.Failure(entity, errorMessage: "Error removing service from bulb", error: ex, requeueAfter: TimeSpan.FromSeconds(30));
            }
            return ReconciliationResult<V1Service>.Success(entity);
        }

        public async Task<ReconciliationResult<V1Service>> ReconcileAsync(V1Service entity, CancellationToken cancellationToken)
        {
            try
            {
                _serviceCache.AddOrUpdate(entity);
                await _bulbReconciler.ReconcileAsync();
                _logger.LogInformation($"Service {entity.Metadata.Name} added/updated in cache and bulb reconciled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding/updating service {entity.Metadata.Name} in cache");
                return ReconciliationResult<V1Service>.Failure(entity, errorMessage: "Error adding/updating service in bulb", error: ex, requeueAfter: TimeSpan.FromSeconds(30));
            }
            return ReconciliationResult<V1Service>.Success(entity);
        }
    }
}