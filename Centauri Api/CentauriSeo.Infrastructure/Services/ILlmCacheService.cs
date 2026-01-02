
namespace CentauriSeo.Infrastructure.Services;

public interface ILlmCacheService
{
    Task<string?> GetAsync(string requestKey);
    Task SaveAsync(string requestKey, string requestText, string responseText, string provider);
    string ComputeRequestKey(string requestText, string provider);
    Task SaveAsync(string requestKey, string responseText);
}