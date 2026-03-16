using Bulb.Contract;
using Bulb.Contract.Caching;
using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;

namespace Bulb.Operators
{
    [EntityRbac(typeof(V1Node), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    public class BulbNodeOperator : IEntityController<V1Node>
    {
        private readonly ICache<V1Node> _nodeCache;
        private readonly IBulbReconciler _bulbReconciler;
        private readonly ILogger _logger;
        
        public BulbNodeOperator(ICache<V1Node> cache, ILogger<BulbNodeOperator> logger, IBulbReconciler bulbReconciler)
        {
            _nodeCache = cache;
            _bulbReconciler = bulbReconciler;
            _logger = logger;        
        }

        public async Task<ReconciliationResult<V1Node>> DeletedAsync(V1Node entity, CancellationToken cancellationToken)
        {
             try
            {
                _nodeCache.Remove(entity);
                await _bulbReconciler.ReconcileAsync();
                _logger.LogInformation($"Node {entity.Metadata.Name} removed from cache and bulb reconciled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing node {entity.Metadata.Name} from cache");
                return ReconciliationResult<V1Node>.Failure(entity, errorMessage: "Error removing node from bulb", error: ex, requeueAfter: TimeSpan.FromSeconds(30));
            }
            return ReconciliationResult<V1Node>.Success(entity);
        }

        public async Task<ReconciliationResult<V1Node>> ReconcileAsync(V1Node entity, CancellationToken cancellationToken)
        {
            try
            {
                _nodeCache.AddOrUpdate(entity);
                await _bulbReconciler.ReconcileAsync();
                _logger.LogInformation($"Node {entity.Metadata.Name} added/updated in cache and bulb reconciled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding/updating node {entity.Metadata.Name} in cache");
                return ReconciliationResult<V1Node>.Failure(entity, errorMessage: "Error adding/updating node in bulb", error: ex, requeueAfter: TimeSpan.FromSeconds(30));
            }
            return ReconciliationResult<V1Node>.Success(entity);
        }
    }
}
