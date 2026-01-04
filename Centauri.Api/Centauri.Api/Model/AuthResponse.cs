namespace Centauri_Api.Model
{

    public class LoginResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string ProfileImage { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; } = 86400;
    }

    public class RegisterResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
        public string VerificationToken { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class SocialLoginResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string ProfileImage { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public bool IsNewUser { get; set; }
    }

    public class VerifyEmailResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
        public DateTime VerifiedAt { get; set; }
    }

    public class SendVerificationEmailResponse
    {
        public string Email { get; set; } = string.Empty;
        public bool VerificationSent { get; set; }
        public int ExpiresIn { get; set; } = 3600;
    }

    public class ChangePasswordResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string Message { get; set; } = "Password changed successfully";
    }

    public class ForgotPasswordResponse
    {
        public string Email { get; set; } = string.Empty;
        public bool ResetTokenSent { get; set; }
        public int ExpiresIn { get; set; } = 3600;
    }

    public class ResetPasswordResponse
    {
        public string Email { get; set; } = string.Empty;
        public bool PasswordReset { get; set; }
    }

    public class RefreshTokenResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; } = 86400;
    }

    public class LogoutResponse
    {
        public string UserId { get; set; } = string.Empty;
        public bool LoggedOut { get; set; } = true;
    }

}
