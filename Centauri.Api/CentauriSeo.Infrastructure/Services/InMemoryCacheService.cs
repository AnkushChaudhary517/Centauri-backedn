using CentauriSeo.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.Services
{
    public class InMemoryCacheService : ILlmCacheService
    {
        private readonly IMemoryCache _cache;
        public InMemoryCacheService(IMemoryCache cache)
        {
            _cache = cache;
        }
        public string ComputeRequestKey(string requestText, string provider)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(provider + ":" + requestText);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public async Task<string?> GetAsync(string requestKey)
        {
            return _cache.TryGetValue<string>(requestKey, out var value) ? value : null;
        }

        public async Task SaveAsync(string requestKey, string responseText)
        {
            var cached = await GetAsync(requestKey);
            if (cached == null)
            {
                _cache.Set(requestKey, responseText, TimeSpan.FromHours(1));
            }
            else
            {
                // Update existing cache entry
                _cache.Set(requestKey, responseText, TimeSpan.FromHours(1));
            }
        }
    }
}
