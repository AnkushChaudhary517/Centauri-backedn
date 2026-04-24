using Centauri_Api.Interface;
using Centauri_Api.Model;
using CentauriSeo.Core.Entitites;
using CentauriSeo.Core.Modules.Notification;
using Microsoft.EntityFrameworkCore;
namespace Centauri_Api.Impl
{


    public class AuthService : IAuthService
    {
        private readonly ITokenService _tokenService;
        private readonly CentauriSeo.Infrastructure.Services.IDynamoDbService _dynamoDbService;
        private readonly EmailVerificationService _verificationService;
        //private readonly IAuthService _authService;

        public AuthService(ITokenService tokenService, CentauriSeo.Infrastructure.Services.IDynamoDbService dynamoDbService,
            EmailVerificationService verificationService)
        {
            _tokenService = tokenService;
            _dynamoDbService = dynamoDbService;
            _verificationService = verificationService;
            //_authService = authService;

        }
        public async Task<LoginResponse> GoogleLoginAsync(GoogleLoginRequest googleLoginRequest)
        {
            var user = await _dynamoDbService.GetUserByEmail(googleLoginRequest.Email);
            if (user == null)
            {
                await RegisterAsync(new RegisterRequest()
                {
                    Email = googleLoginRequest.Email,
                    AcceptTerms = true,
                    IsGoogleLogin = true,
                    FirstName = googleLoginRequest.Name
                });
                user = await _dynamoDbService.GetUserByEmail(googleLoginRequest.Email);
                await _dynamoDbService.CreateUserSubscription(new CentauriSeo.Core.Entitites.CentauriUserSubscription()
                {
                    UserId = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    MidEmailSent = false,
                    Reminder48hAt = DateTime.UtcNow.AddDays(12),
                    Reminder48hSent = false,
                    Status = "active",
                    TrialEndedEmailSent = false,
                    TrialEndsAt = DateTime.UtcNow.AddDays(14),
                    TrialStartAt = DateTime.UtcNow
                });
                try
                {
                    await _verificationService.SendVerificationCodeAsync(googleLoginRequest.Email, "freetrial", user.FirstName, 0, true);

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating user subscription: {ex.Message}");
                }
            }
            var token = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            var response = new LoginResponse
            {
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Token = token,
                RefreshToken = refreshToken,
                ExpiresIn = 3600
            };
            return response;
        }

        public async Task<bool> DeleteUserAsync(string userId, string email)
        {
            await _dynamoDbService.DeleteUserAsync(userId, email);
            return true;
        }
        public async Task<(bool success, LoginResponse? response, string? error)> LoginAsync(LoginRequest request)
        {
            try
            {
                var user = await _dynamoDbService.GetUserByEmail(request.Email);

                if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    return (false, null, "INVALID_CREDENTIALS");
                }

                var token = _tokenService.GenerateAccessToken(user);
                var refreshToken = _tokenService.GenerateRefreshToken();

                var response = new LoginResponse
                {
                    UserId = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Token = token,
                    RefreshToken = refreshToken,
                    ExpiresIn = 86400
                };

                return (true, response, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public async Task<(bool success, RegisterResponse? response, string? error)> RegisterAsync(RegisterRequest request)
        {
            try
            {
                var existinguser = await _dynamoDbService.GetUserByEmail(request.Email);
                if (existinguser != null)
                {
                    return (false, null, "EMAIL_ALREADY_EXISTS");
                }

                //var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
                //if (existingUser != null)
                //{
                //    return (false, null, "EMAIL_ALREADY_EXISTS");
                //}

                var user = new CentauriUser
                {
                    Email = request.Email?.ToLower(),
                    //FirstName = request.Name.Split(' ')[0],
                    //LastName = request.Name.Contains(' ') ? request.Name.Split(' ')[1] : "",
                    PasswordHash =request.IsGoogleLogin? GetTemporaryPassword(request.Email) : BCrypt.Net.BCrypt.HashPassword(request.Password),
                    VerificationToken = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Plan = "FREE",
                    TrialEndsAt = DateTime.UtcNow.AddDays(14),
                    CreditsAdded = 5,
                    IsGoogleLogin = request.IsGoogleLogin
                };


                await _dynamoDbService.CreateUserAsync(user);

                //_context.Users.Add(user);
                //await _context.SaveChangesAsync();

                var response = new RegisterResponse
                {
                    UserId = user.Id,
                    Email = user.Email,
                    //Name = request.Name,
                    EmailVerified = false,
                    VerificationToken = user.VerificationToken,
                    CreatedAt = user.CreatedAt
                };

                return (true, response, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private string GetTemporaryPassword(string email)
        {
            return BCrypt.Net.BCrypt.HashPassword($"test_get_centauri_user_{email}");
        }

        public async Task<(bool success, RefreshTokenResponse? response, string? error)> RefreshTokenAsync(RefreshTokenRequest request)
        {
            try
            {
                // For demo purposes, accept any refresh token and generate new one
                // In production, validate and store refresh tokens in database

                var newAccessToken = _tokenService.GenerateAccessToken(new CentauriUser
                {
                    Id = "user_temp",
                    Email = "temp@demo.com",
                    FirstName = "Demo",
                    LastName = "User"
                });
                var newRefreshToken = _tokenService.GenerateRefreshToken();

                var response = new RefreshTokenResponse
                {
                    Token = newAccessToken,
                    RefreshToken = newRefreshToken,
                    ExpiresIn = 86400
                };

                return (true, response, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public async Task<(bool success, LogoutResponse? response, string? error)> LogoutAsync(string userId)
        {
            try
            {
                // For demo purposes, just return success
                var response = new LogoutResponse
                {
                    UserId = userId,
                    LoggedOut = true
                };

                return (true, response, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

    }

}
