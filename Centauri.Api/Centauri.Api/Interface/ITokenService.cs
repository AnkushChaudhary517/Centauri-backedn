using Centauri_Api.Entitites;

namespace Centauri_Api.Interface
{
    public interface ITokenService
    {
        string GenerateAccessToken(CentauriUser user);
        string GenerateRefreshToken();
        bool ValidateToken(string token);
    }
}
