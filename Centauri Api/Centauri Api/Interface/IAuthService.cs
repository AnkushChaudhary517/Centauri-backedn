using Centauri_Api.Model;

namespace Centauri_Api.Interface
{
    public interface IAuthService
    {
        Task<(bool success, LoginResponse? response, string? error)> LoginAsync(LoginRequest request);
        Task<(bool success, RegisterResponse? response, string? error)> RegisterAsync(RegisterRequest request);
        Task<(bool success, LogoutResponse? response, string? error)> LogoutAsync(string userId);
        Task<LoginResponse> GoogleLoginAsync(GoogleLoginRequest googleLoginRequest);
    }
}
