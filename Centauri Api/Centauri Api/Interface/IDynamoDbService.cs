using Centauri_Api.Entitites;
using static System.Net.Mime.MediaTypeNames;

namespace Centauri_Api.Interface
{
    public interface IDynamoDbService
    {
        Task CreateUserAsync(CentauriUser user);
        Task<CentauriUser?> GetUserAsync(string userId);
        Task<List<CentauriUser>> GetAllUsersAsync();
        Task UpdateUserAsync(CentauriUser user);
        Task DeleteUserAsync(string userId);
        Task<CentauriUser?> GetUserByEmail(string email);
      
    }
}
