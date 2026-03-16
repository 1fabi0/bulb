using k8s;
using k8s.Models;

namespace Bulb.Contract.Caching
{
    public interface ICache<T> where T : IKubernetesObject<V1ObjectMeta>
    {
        void AddOrUpdate(T item);
        void Remove(T item);
        IEnumerable<T> Get(string? @namespace = null);
        T? Get(string name, string @namespace);
    }
}
