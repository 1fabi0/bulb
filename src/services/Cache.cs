using Bulb.Contract.Caching;
using k8s;
using k8s.Models;

namespace Bulb.Services
{
    public class Cache<T> : ICache<T> where T : IKubernetesObject<V1ObjectMeta>
    {
        private readonly Dictionary<(string name, string @namespace), T> _cache = new Dictionary<(string name, string @namespace), T>();
        public void AddOrUpdate(T item)
        {
            _cache[(item.Metadata.Name, item.Metadata.Namespace() ?? "default")] = item;
        }

        public IEnumerable<T> Get(string? @namespace = null)
        {
            if (@namespace == null)
            {
                return _cache.Values;
            }
            else
            {
                return _cache.Where(item => item.Key.@namespace == @namespace).Select(item => item.Value);
            }
        }

        public T? Get(string name, string @namespace)
        {
            return _cache.GetValueOrDefault((name, @namespace));
        }

        public void Remove(T item)
        {
            _cache.Remove((item.Metadata.Name, item.Metadata.Namespace() ?? "default"));
        }
    }
}
