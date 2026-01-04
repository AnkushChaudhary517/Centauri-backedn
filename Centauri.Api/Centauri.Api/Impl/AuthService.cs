using Centauri_Api.Entitites;
using Centauri_Api.Interface;
using Centauri_Api.Model;
using Microsoft.EntityFrameworkCore;
namespace Centauri_Api.Impl
{


    public class AuthService : IAuthService
    {
        private readonly ITokenService _tokenService;
        private readonly IDynamoDbService _dynamoDbService;

        public AuthService(ITokenService tokenService, IDynamoDbService dynamoDbService)
        {
            _tokenService = tokenService;
            _dynamoDbService = dynamoDbService;

        }
        public async Task<LoginResponse> GoogleLoginAsync(GoogleLoginRequest googleLoginRequest)
        {
            var user = await _dynamoDbService.GetUserByEmail(googleLoginRequest.Email);
            if (user == null)
            {
                await RegisterAsync(new RegisterRequest()
                {
                    Email = googleLoginRequest.Email,
                    Name = googleLoginRequest.Name,
                    Password = Guid.NewGuid().ToString(),
                    AcceptTerms = true
                });
                user = await _dynamoDbService.GetUserByEmail(googleLoginRequest.Email);
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
                    Email = request.Email,
                    FirstName = request.Name.Split(' ')[0],
                    LastName = request.Name.Contains(' ') ? request.Name.Split(' ')[1] : "",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    VerificationToken = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                await _dynamoDbService.CreateUserAsync(user);
                //_context.Users.Add(user);
                //await _context.SaveChangesAsync();

                var response = new RegisterResponse
                {
                    UserId = user.Id,
                    Email = user.Email,
                    Name = request.Name,
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
