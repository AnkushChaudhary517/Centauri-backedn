using System.Text.Json.Serialization;

namespace Centauri_Api.Model
{

    public class LoginRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
        [JsonPropertyName("acceptTerms")]
        public bool AcceptTerms { get; set; } = true;
        [JsonPropertyName("isGoogleLogin")]
        public bool IsGoogleLogin { get; set; } = false;
    }

    public class UpdateProfileRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
        [JsonPropertyName("firstName")]
        public string FirstName { get; set; }
        [JsonPropertyName("lastName")]
        public string LastName { get; set; }
        [JsonPropertyName("company")]
        public string Company { get; set; }
        [JsonPropertyName("contactNumber")]
        public string ContactNumber { get; set; }
    }
    public class GoogleLoginRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class SocialLoginRequest
    {
        public string Provider { get; set; } = string.Empty; // google, facebook, apple
        public string IdToken { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
    }

    public class VerifyEmailRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class SendVerificationEmailRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string ResetToken { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
    public class CreditsAddRequest
    {
        public string Plan { get; set; } = "regular";
    }
    public class SelectSubscriptionRequest
    {
        public string PlanId { get; set; }
        public string PlanName { get; set; }
        public string Plan { get; set; }
        public string BillingCycle { get; set; }
        public decimal MonthlyPrice { get; set; }
        public int ArticleAnalysesPerMonth { get; set; }
    }

    public class RazorPayOrderRequest
    {
        public decimal MonthlyPrice { get; set; }
    }
    public class RazorPayOrderResponse
    {
        public string OrderId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string KeyId { get; set; }
    }

    public class CreditsAvailableResponse
    {
        public int Available { get; set; }
        public int Used { get; set; }
        public int Total { get; set; }
        public string ExpiresAt { get; set; }
    }
    public class CurrentSuscription
    {
        public string PlanId { get; set; }
        public string Name { get; set; }
        public string PriceLabel { get; set; }
        public string BillingCycle { get; set; }
        public decimal MonthlyPrice { get; set; }
        public string Status { get; set; }
        public string RenewalDate { get; set; }
        public int ArticleAnalysesPerMonth { get; set; }
    }
}
