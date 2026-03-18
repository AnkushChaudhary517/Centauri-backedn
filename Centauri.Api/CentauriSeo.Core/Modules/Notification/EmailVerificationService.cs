using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Modules.Notification
{
    // Services/EmailVerificationService.cs

    public class EmailVerificationService
    {
        private readonly IEmailSender _emailSender;
        private readonly IEmailVerificationRepository _repository;

        public EmailVerificationService(
            IEmailSender emailSender,
            IEmailVerificationRepository repository)
        {
            _emailSender = emailSender;
            _repository = repository;
        }

        public async Task SendVerificationCodeAsync(string email, string type="EmailVerification")
        {
            var code = GenerateCode();

            var verification = new EmailVerificationCode
            {
                Email = email,
                Code = code,
                ExpiryTime = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
                IsUsed = false,
                Type = type
            };

            await _repository.SaveCodeAsync(verification);
            
            var subject = "Your verification code";

            var body = $@"
            <h2>Email Verification</h2>
            <p>Your verification code is:</p>
            <h3>{code}</h3>
            <p>This code will expire in 10 minutes.</p>
        ";

            if (type.ToLower() == "forgotpassword")
            {
                subject = "Your password reset code";
                body = $@"
                <h2>Password Reset</h2>
                <p>Your password reset code is:</p>
                <h3>{code}</h3>
                <p>This code will expire in 10 minutes.</p>";
            }
            await _emailSender.SendEmailAsync(email, subject, body);
        }

        public async Task<bool> VerifyCodeAsync(string email, string code,string type= "EmailVerification")
        {
            var record = await _repository.GetCodeAsync(email);

            if (record == null)
                return false;

            if (record.IsUsed)
                return false;

            if (record.Code != code)
                return false;

            if (record.ExpiryTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return false;

            await _repository.MarkUsedAsync(email);

            return true;
        }

        private string GenerateCode()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }
    }
}
