using Bulb.Configuration;
using Bulb.Services.Listener;
using k8s.Models;
using KubeOps.Abstractions.Builder;
using KubeOps.Operator;
using Bulb.Contract;
using Bulb.Services;
using Bulb.Services.Status;
using Bulb.Contract.Caching;
using Bulb.Operators;

namespace Bulb
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builderListener = Host.CreateApplicationBuilder(args);

            var bulbConfig = BulbConfigurationValidator.GetBulbConfiguration(builderListener.Configuration);

            builderListener.Services.AddMemoryCache();
            builderListener.Services.AddSingleton(bulbConfig);
            if(bulbConfig.BackendSystem == "ipvs")
            {
                builderListener.Services.AddSingleton<ILoadBalancerBackendService, IpVsBackendService>();
            }
            else
            {
                //todo: implement iptables backend service
                throw new NotSupportedException($"Backend system {bulbConfig.BackendSystem} is not yet supported.");
            }
            builderListener.Services.AddSingleton<IBulbReconciler, ListenerReconciler>();
            builderListener.Services.AddSingleton<IServiceEndpointResolver, ServiceEndpointResolver>();
            builderListener.Services.AddSingleton<ICache<V1EndpointSlice>, Cache<V1EndpointSlice>>();
            builderListener.Services.AddSingleton<ICache<V1Service>, Cache<V1Service>>();
            builderListener.Services.AddSingleton<ICache<V1Node>, Cache<V1Node>>();

            builderListener.Services.AddKubernetesOperator(op =>
            {
                op.LeaderElectionType = LeaderElectionType.None;
                op.Name = "bulb-listener";
            })
                .AddController<BulbServiceOperator, V1Service>()
                .AddController<BulbEndpointSliceOperator, V1EndpointSlice>()
                .AddController<BulbNodeOperator, V1Node>();

            var listenerHost = builderListener.Build();

            if(bulbConfig.IsServiceStatusUpdateEnabled)
            {
                var builderLeader = Host.CreateApplicationBuilder(args);
                builderLeader.Services.AddMemoryCache();
                builderLeader.Services.AddSingleton(bulbConfig);

                builderLeader.Services.AddSingleton<IBulbReconciler, StatusReconciler>();
                builderLeader.Services.AddSingleton<ICache<V1Service>, Cache<V1Service>>();
                builderLeader.Services.AddSingleton<ICache<V1Node>, Cache<V1Node>>();

                builderLeader.Services.AddKubernetesOperator(op =>
                {
                    op.LeaderElectionType = LeaderElectionType.Single;
                    op.Name = "bulb-service-status";
                })
                    .AddController<BulbServiceOperator, V1Service>()
                    .AddController<BulbNodeOperator, V1Node>();

                var leaderHost = builderLeader.Build();

                await Task.WhenAll(
                    leaderHost.RunAsync(),
                    listenerHost.RunAsync()
                );
            }
            else
            {
                await listenerHost.RunAsync();
            }
        }
    }
}

