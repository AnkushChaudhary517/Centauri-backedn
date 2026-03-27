
using Centauri_Api.Interface;
using Centauri_Api.Model;
using CentauriSeo.Core.Modules.Notification;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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
    private readonly EmailVerificationService _verificationService;

    public AuthController(IAuthService authService, IDynamoDbService dynamoDbService,
        IConfiguration config, IHttpClientFactory httpClientFactory,
        EmailVerificationService verificationService
        )
    {
        _authService = authService;
        _dynamoDbService = dynamoDbService;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _verificationService = verificationService;
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
        if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
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

    [HttpPost("updateprofile")]
    public async Task<ActionResult<ApiResponse<RegisterResponse>>> UpdateProfile(UpdateProfileRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(ApiResponseHelper.Error<RegisterResponse>(
                "INVALID_REQUEST",
                "Invalid update request. Please provide all required fields.",
                400
            ));
        }
        var existingUser = await _dynamoDbService.GetUserByEmail(request.Email.ToLower());
        if(existingUser != null)
        {
            return BadRequest(ApiResponseHelper.Error<RegisterResponse>(
               "INVALID_REQUEST",
               "User Already exists",
               400
           ));
        }
        var user = new Entitites.CentauriUser
        {
            Email = request.Email?.ToLower(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Company = request.Company,
            ContactNumber = request.ContactNumber,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreditsAdded = 5,
            EmailVerified = true,
            Id = Guid.NewGuid().ToString(),
            Plan = "free-trial",
            SubscriptionEndsAt = DateTime.UtcNow.AddDays(14),
            TrialEndsAt = DateTime.UtcNow.AddDays(14)
            
        };
         await _dynamoDbService.CreateUserAsync(user);

        return CreatedAtAction(nameof(Register), ApiResponseHelper.Success("Account created successfully. Please verify your email."));
    }
    [HttpPost("send-verification")]
    public async Task<IActionResult> SendVerification([FromBody] string email)
    {
        await _verificationService.SendVerificationCodeAsync(email);

        return Ok(new { message = "Verification code sent" });
    }
    [Authorize]
    [HttpPost("add-credits")]
    public async Task<IActionResult> AddCredits([FromBody] CreditsAddRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await _dynamoDbService.GetUserAsync(userId);
        if (user == null)
        {
            throw new Exception("Wrong user");
        }
        if(request?.Plan?.ToLower()=="regular")
        {
            user.CreditsAdded += 15;
            user.SubscriptionEndsAt = DateTime.UtcNow.AddDays(15);
            user.TrialEndsAt = DateTime.UtcNow.AddDays(15);
            await _dynamoDbService.UpdateUserAsync(user);
        }
        
        return Ok(new { message = "Credits added successfully" });
    }

[Authorize]
    [HttpGet("subscription/current")]
    public async Task<IActionResult> GetCurrentSubscription()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await _dynamoDbService.GetUserAsync(userId);
        if (user == null)
        {
            throw new Exception("Wrong user");
        }
        var isTrial = true;
        
        var response = new CurrentSuscription()
        {
            PlanId = "trial-14-day",
            ArticleAnalysesPerMonth = 5,
            BillingCycle = "monthly",
            MonthlyPrice = 0,
            Name ="Free Trial",
            PriceLabel = "Free",
            RenewalDate = user.SubscriptionEndsAt.ToShortDateString(),
            Status = "trial"
        };
        if (!user.Plan.ToLower().Contains("trial"))
        {
            response = new CurrentSuscription()
            {
                PlanId = "starter",
                ArticleAnalysesPerMonth = 10,
                BillingCycle = "monthly",
                MonthlyPrice = 15,
                Name = "Starter",
                PriceLabel = "15$",
                RenewalDate = user.SubscriptionEndsAt.ToShortDateString(),
                Status = "starter"
            };
        }
        return Ok(response);
    }
    [HttpGet("credits/remaining")]
    [Authorize]
    public async Task<IActionResult> GetAvailableCredits()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await _dynamoDbService.GetUserAsync(userId);
        if (user == null)
        {
            throw new Exception("Wrong user");
        }
        var total = user.Plan.ToLower().Contains("trial") ? 5 : 10;
        var availabelle = user.CreditsAdded;
        return Ok(new CreditsAvailableResponse()
        {
            Available = availabelle,
            Total = total,
            Used = total-availabelle,
            ExpiresAt = user.SubscriptionEndsAt.ToShortDateString()
        });
    }

    [HttpPost("subscription/select")]
    [Authorize]
    public async Task<IActionResult> SelectSubscription([FromBody] SelectSubscriptionRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await _dynamoDbService.GetUserAsync(userId);
        if (user == null)
        {
            throw new Exception("Wrong user");
        }
        // Basic validation
        if (request == null || string.IsNullOrEmpty(request.PlanId))
            return BadRequest("Invalid request");

        // TODO: Save to DB / call service
        if (request?.Plan?.ToLower() == "starter-monthly")
        {
            user.CreditsAdded += 10;
            user.SubscriptionEndsAt = DateTime.UtcNow.AddDays(30);
            user.TrialEndsAt = DateTime.UtcNow.AddDays(30);
            user.Plan = "starter-monthly";
            await _dynamoDbService.UpdateUserAsync(user);
        }

        return Ok(new { message = "Starter monthly Subscription added successfully" });
    }
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] string email)
    {
        await _verificationService.SendVerificationCodeAsync(email,"ForgotPassword");

        return Ok(new { message = "Verification code sent" });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var success = await _verificationService.VerifyCodeAsync(request.Email, request.ResetToken);
        if(!success)
        {
            return BadRequest(ApiResponseHelper.Error<RegisterResponse>(
              "INVALID_REQUEST",
              "Wrong verification code provided",
              400
          ));
        }
        var user = await _dynamoDbService.GetUserByEmail(request.Email);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _dynamoDbService.UpdateUserAsync(user);
        return Ok(new { message = "password reset successfully" });
    }
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody]Model.VerifyEmailRequest request)
    {
        var result = await _verificationService.VerifyCodeAsync(request?.Email, request?.Code);

        if (!result)
            return BadRequest("Invalid or expired code");

        return Ok(new
        {
            Success = result,
            Message = "Email verified"
        });
    }
    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<LogoutResponse>>> Logout(RefreshTokenRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(ApiResponseHelper.Error<LogoutResponse>(
                "UNAUTHORIZEAUTHORIZED",
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
            $"&redirect_uri={redirect_uri}" +
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
                { "redirect_uri", $"{Uri.EscapeDataString(GetCallbackUrl())}" },
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
        //return "https://api.thedigna.com/api/v1/facebook/auth/callback";
        return $"http://ec2-13-126-103-12.ap-south-1.compute.amazonaws.com:3000/api/v1/auth/callback";

    }

    private string GetCallbackUrl()
    {
        //return $"http://ec2-13-126-103-12.ap-south-1.compute.amazonaws.com:3000/api/v1/auth/callback";
        return $"{Request.Scheme}://{Request.Host}/api/v1/auth/callback";
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
