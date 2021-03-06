using Halibut.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halibut.Transport
{
    public class ConnectionPool<TKey, TPooledResource>
        where TPooledResource : class, IPooledResource
    {
        readonly Dictionary<TKey, HashSet<TPooledResource>> pool = new Dictionary<TKey, HashSet<TPooledResource>>();

        public int GetTotalConnectionCount()
        {
            lock (pool)
            {
                return pool.Values.Sum(v => v.Count);
            }
        }

        public TPooledResource Take(TKey endPoint)
        {
            lock (pool)
            {
                var connections = GetOrAdd(endPoint);

                while (true)
                {
                    var connection = Take(connections);

                    if (connection == null || !connection.HasExpired())
                        return connection;

                    DestroyConnection(connection, null);
                }
            }
        }

        public void Return(TKey endPoint, TPooledResource resource)
        {
            lock (pool)
            {
                var connections = GetOrAdd(endPoint);
                connections.Add(resource);
                resource.NotifyUsed();

                while (connections.Count > 5)
                {
                    var connection = Take(connections);
                    DestroyConnection(connection, null);
                }
            }
        }

        public void Clear(TKey key, ILog log = null)
        {
            lock (pool)
            {
                if (!pool.TryGetValue(key, out var connections))
                    return;

                foreach (var connection in connections)
                {
                    DestroyConnection(connection, log);
                }

                connections.Clear();
            }
        }

        public void Dispose()
        {
            lock (pool)
            {
                foreach (var connection in pool.SelectMany(kv => kv.Value))
                {
                    DestroyConnection(connection, null);
                }

                pool.Clear();
            }
        }

        private TPooledResource Take(HashSet<TPooledResource> connections)
        {
            if (connections.Count == 0)
                return null;

            var connection = connections.First();
            connections.Remove(connection);
            return connection;
        }

        private HashSet<TPooledResource> GetOrAdd(TKey endPoint)
        {
            if (!pool.TryGetValue(endPoint, out var connections))
            {
                connections = new HashSet<TPooledResource>();
                pool.Add(endPoint, connections);
            }

            return connections;
        }

        private void DestroyConnection(TPooledResource connection, ILog log)
        {
            try
            {
                connection?.Dispose();
            }
            catch (Exception ex)
            {
                log?.WriteException(EventType.Error, "Exception disposing connection from pool", ex);
            }
        }
    }
}