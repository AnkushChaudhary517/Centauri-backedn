using CentauriSeo.Core.Entitites;
using CentauriSeo.Core.Models.Output;
using static System.Net.Mime.MediaTypeNames;

namespace CentauriSeo.Infrastructure.Services
{
    public interface IDynamoDbService
    {
        Task CreateUserAsync(CentauriUser user);
        Task<CentauriUser?> GetUserAsync(string userId);

        Task SavePastResponseForUser(string userId,string requestId, string response);
        Task<List<PastAnalysisResponse>> GetPastResponsesForUser(string userId);    
        Task<List<CentauriUser>> GetAllUsersAsync();
        Task UpdateUserAsync(CentauriUser user);
        Task DeleteUserAsync(string userId, string email);
        Task<CentauriUser?> GetUserByEmail(string email);
        Task CreateUserSubscription(CentauriUserSubscription centauriUserSubscription);

        Task<string> GetPrompt(string promptName);
    }
}
