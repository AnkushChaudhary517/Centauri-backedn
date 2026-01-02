using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CentauriSeo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentauriSeo.Infrastructure.Services;

public class LlmCacheService : ILlmCacheService
{
    private readonly LlmCacheDbContext _db;

    public LlmCacheService(LlmCacheDbContext db)
    {
        _db = db;
    }

    public string ComputeRequestKey(string requestText, string provider)
    {
        // key = SHA256(provider + ":" + requestText)
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(provider + ":" + requestText);
        var hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public async Task<string?> GetAsync(string requestKey)
    {
        var entry = await _db.LlmCacheEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.RequestKey == requestKey);
        return entry?.ResponseText;
    }

    public async Task SaveAsync(string requestKey, string requestText, string responseText, string provider)
    {
        var entry = new LlmCacheEntry
        {
            RequestKey = requestKey,
            RequestText = requestText,
            ResponseText = responseText,
            Provider = provider,
            CreatedAt = DateTime.UtcNow
        };

        _db.LlmCacheEntries.Add(entry);
        await _db.SaveChangesAsync();
    }
}