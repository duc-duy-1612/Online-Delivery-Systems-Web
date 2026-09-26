using System;
using System.Web;
using System.Threading.Tasks;

namespace ĐACN.Services
{
    public class CacheService
    {
        /// <summary>
        /// Retrieves an item from the cache or executes the provided factory method to get it,
        /// then stores it in the cache for the specified duration.
        /// </summary>
        public async Task<T> GetOrSetAsync<T>(string key, int durationInMinutes, Func<Task<T>> factory)
        {
            var cachedObj = HttpRuntime.Cache[key];
            if (cachedObj != null)
            {
                return (T)cachedObj;
            }

            T result = await factory();

            if (result != null)
            {
                HttpRuntime.Cache.Insert(
                    key, 
                    result, 
                    null, 
                    DateTime.Now.AddMinutes(durationInMinutes), 
                    System.Web.Caching.Cache.NoSlidingExpiration);
            }

            return result;
        }
        
        /// <summary>
        /// Synchronous version of GetOrSet
        /// </summary>
        public T GetOrSet<T>(string key, int durationInMinutes, Func<T> factory)
        {
            var cachedObj = HttpRuntime.Cache[key];
            if (cachedObj != null)
            {
                return (T)cachedObj;
            }

            T result = factory();

            if (result != null)
            {
                HttpRuntime.Cache.Insert(
                    key, 
                    result, 
                    null, 
                    DateTime.Now.AddMinutes(durationInMinutes), 
                    System.Web.Caching.Cache.NoSlidingExpiration);
            }

            return result;
        }

        /// <summary>
        /// Removes an item from the cache.
        /// </summary>
        public void Remove(string key)
        {
            if (HttpRuntime.Cache[key] != null)
            {
                HttpRuntime.Cache.Remove(key);
            }
        }
    }
}
