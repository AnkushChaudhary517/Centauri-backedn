
using Centauri_Api.Interface;
using Centauri_Api.Model;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ForgotPasswordRequest = Centauri_Api.Model.ForgotPasswordRequest;
using LoginRequest = Centauri_Api.Model.LoginRequest;
using RegisterRequest = Centauri_Api.Model.RegisterRequest;
using ResetPasswordRequest = Centauri_Api.Model.ResetPasswordRequest;

namespace Centauri_Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IDynamoDbService _dynamoDbService;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthController(IAuthService authService, IDynamoDbService dynamoDbService,
        IConfiguration config, IHttpClientFactory httpClientFactory
        )
    {
        _authService = authService;
        _dynamoDbService = dynamoDbService;
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(LoginRequest request)
    {

        var (success, response, error) = await _authService.LoginAsync(request);

        if (!success)
        {
            return BadRequest(ApiResponseHelper.Error<LoginResponse>(
                error ?? "LOGIN_FAILED",
                "Login failed. Please check your credentials.",
                400
            ));
        }

        return Ok(ApiResponseHelper.Success(response, "Login successful"));
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<RegisterResponse>>> Register(RegisterRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(ApiResponseHelper.Error<RegisterResponse>(
                "INVALID_REQUEST",
                "Invalid registration request. Please provide all required fields.",
                400
            ));
        }

        var (success, response, error) = await _authService.RegisterAsync(request);

        if (!success)
        {
            return BadRequest(ApiResponseHelper.Error<RegisterResponse>(
                error ?? "REGISTRATION_FAILED",
                error == "EMAIL_ALREADY_EXISTS"
                    ? "An account with this email already exists"
                    : "Registration failed. Please try again.",
                400
            ));
        }

        return CreatedAtAction(nameof(Register), ApiResponseHelper.Success("Account created successfully. Please verify your email."));
    }
    //[Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<LogoutResponse>>> Logout(RefreshTokenRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(ApiResponseHelper.Error<LogoutResponse>(
                "UNAUTHORIZED",
                "User not authenticated",
                401
            ));
        }

        var (success, response, error) = await _authService.LogoutAsync(userId);

        if (!success)
        {
            return BadRequest(ApiResponseHelper.Error<LogoutResponse>(
                error ?? "LOGOUT_FAILED",
                "Failed to logout",
                400
            ));
        }

        return Ok(ApiResponseHelper.Success(response, "Logged out successfully"));
    }
    // STEP 1: Redirect to Google
    [HttpGet("google")]
    public IActionResult GoogleLogin([FromQuery] string redirect_uri)
    {
        var googleAuthUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            "?response_type=code" +
            $"&client_id={_config["GoogleAuth:ClientId"]}" +
            $"&redirect_uri={Uri.EscapeDataString(GetCallbackUrl())}" +
            "&scope=openid%20email%20profile" +
            "&access_type=offline" +
            "&prompt=consent" +
            $"&state={Uri.EscapeDataString(redirect_uri)}";

        return Redirect(googleAuthUrl);
    }

    // STEP 2: Google callback
    [HttpGet("callback")]
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string code,
        [FromQuery] string state)
    {
        var client = _httpClientFactory.CreateClient();

        // Exchange code for tokens
        var tokenResponse = await client.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", _config["GoogleAuth:ClientId"] },
                { "client_secret", _config["GoogleAuth:ClientSecret"] },
                { "redirect_uri", GetCallbackUrl() },
                { "grant_type", "authorization_code" }
            })
        );

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);
        Console.WriteLine(tokenData.ToString());
        var idToken = tokenData.GetProperty("id_token").GetString();

        // OPTIONAL: Validate ID token (recommended)
        // You can use GoogleJsonWebSignature here

        // TODO:
        // 1. Create / find user
        // 2. Generate your own JWT



        // Redirect back to frontend
        //return Redirect($"{state}?token={idToken}");
        // After creating user and generating JWT tokens:
        return Redirect($"{state}?token={Uri.EscapeDataString(idToken)}");
    }
    [HttpPost("google/exchange")]
    public async Task<IActionResult> ExchangeGoogleToken([FromBody] GoogleTokenExchangeRequest request)
    {
        // Validate Google ID token
        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { _config["GoogleAuth:ClientId"] }
        };

        var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);

        var response = await _authService.GoogleLoginAsync(new GoogleLoginRequest()
        {
            Name = payload.Name,
            Email = payload.Email
        });

        return Ok(new
        {
            success = true,
            data = new
            {
                userId = response.UserId,
                email = response.Email,
                firstName = response.FirstName,
                lastName = response.LastName,
                profileImage = response.ProfileImage ?? payload.Picture,
                token = response.Token,
                refreshToken = response.RefreshToken
            }
        });
    }

    [HttpGet("facebook")]
    public IActionResult FacebookLogin([FromQuery] string redirect_uri)
    {
        var facebookAuthUrl =
            "https://www.facebook.com/v18.0/dialog/oauth" +
            $"?client_id={_config["FacebookAuth:AppId"]}" +
            $"&redirect_uri={GetFacebookCallbackUrl()}" +
            "&scope=email,public_profile" +
            $"&state={Uri.EscapeDataString(redirect_uri)}";

        return Redirect(facebookAuthUrl);
    }
    [HttpGet("facebook/callback")]
    public async Task<IActionResult> FacebookCallback(
    [FromQuery] string code,
    [FromQuery] string state)
    {
        var client = _httpClientFactory.CreateClient();
        var r = "https://graph.facebook.com/v18.0/oauth/access_token" +
            $"?client_id={_config["FacebookAuth:AppId"]}" +
            $"&redirect_uri={GetFacebookCallbackUrl()}" +
            $"&client_secret={_config["FacebookAuth:AppSecret"]}" +
            $"&code={code}";
        var res = await client.GetAsync(r);
        var tokenResponse = await res.Content.ReadAsStringAsync();

        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenResponse);
        var accessToken = tokenData.GetProperty("access_token").GetString();

        // Fetch user info
        var userResponse = await client.GetStringAsync(
            "https://graph.facebook.com/me" +
            $"?fields=id,name,email,picture" +
            $"&access_token={accessToken}"
        );

        var userData = JsonSerializer.Deserialize<JsonElement>(userResponse);

        var email = userData.GetProperty("email").GetString();
        var name = userData.GetProperty("name").GetString();
        var facebookId = userData.GetProperty("id").GetString();

        //// TODO:
        //// 1. Create or find user
        //// 2. Generate your JWT

        //var response = await _authService.GoogleLoginAsync(new GoogleLoginRequest()
        //{
        //    Name = name,
        //    Email = email
        //});

        return Redirect($"{state}?email={email}&token={accessToken}&provider=facebook");
    }

    public class GoogleTokenExchangeRequest
    {
        public string IdToken { get; set; }
    }

    private string GetFacebookCallbackUrl()
    {
        return "https://api.thedigna.com/api/v1/facebook/auth/callback";
        //return $"{Request.Scheme}://{Request.Host}/api/v1/auth/facebook/callback";
    }

    private string GetCallbackUrl()
    {
        return "https://localhost:7206/api/v1/auth/callback";
        //return $"{Request.Scheme}://{Request.Host}/api/v1/auth/callback";
    }
    private async Task<GoogleJsonWebSignature.Payload> ValidateIdToken(string idToken)
    {
        return await GoogleJsonWebSignature.ValidateAsync(
            idToken,
            new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _config["GoogleAuth:ClientId"] }
            });
    }
}
