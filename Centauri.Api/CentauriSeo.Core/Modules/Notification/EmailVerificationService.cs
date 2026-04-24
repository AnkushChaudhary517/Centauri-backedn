using Amazon.S3;
using Amazon.S3.Model;
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

        public async Task SendVerificationCodeAsync(string email, string type="EmailVerification", string firstName=null, int hoursLeft=0, bool fromGoogleAuth=false)
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

            if(type.ToLower()=="signup")
            {
                (subject, body) = GetSignUpSubjectAndBody(code);
            }
            else if (type.ToLower() == "forgotpassword")
            {
                (subject, body) = GetForgotPassowrdSubjectAndBody(code);
            }
            else if(type.ToLower() == "freetrial" && (!string.IsNullOrEmpty(firstName)|| fromGoogleAuth))
            {
                (subject, body) = GetFreeTrialSubjectAndBody(firstName);
                var path = "Resources\\Centauri Ideal Upload File Sample.docx";
                await _emailSender.SendEmailAsync(email, subject, body,path);
                return;
            } 
            else if(type.ToLower() == "midtrial" && !string.IsNullOrEmpty(firstName))
            {
                (subject, body) = GetMidTrialSubjectAndBody(firstName);
            }
            else if(type.ToLower() == "trialending" && !string.IsNullOrEmpty(firstName) && hoursLeft > 0)
            {
                    (subject, body) = GetTrialEndingInHoursSubjectAndBody(firstName, hoursLeft);
            }
            else if(type.ToLower() == "trialended" && !string.IsNullOrEmpty(firstName))
            {
                (subject, body) = GetTrialEndedSubjectAndBody(firstName);
            }

            await _emailSender.SendEmailAsync(email, subject, body);
        }
        private (string subject, string body) GetTrialEndedSubjectAndBody(string firstName)
        {
            string subject = "Your Centauri trial has ended";

            string body = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Trial Ended</title>
</head>
<body style='margin:0; padding:0; font-family: Arial, sans-serif; background-color:#f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4; padding:20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff; padding:30px; border-radius:8px;'>

                    <tr>
                        <td style='font-size:18px; color:#333333;'>
                            Hi {{FirstName}},
                        </td>
                    </tr>

                    <tr><td height='15'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.6;'>
                            Your <strong>Centauri free trial</strong> has ended.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.6;'>
                            You can still access your previous analyses, but to continue analyzing new articles, you’ll need to move to a paid plan.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:15px; color:#333333; font-weight:bold;'>
                            Here’s what you get:
                        </td>
                    </tr>

                    <tr><td height='10'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.8;'>
                            • 10 article analyses per month<br/>
                            • Access to full scoring and recommendations<br/>
                            • Continuous improvements across your content
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td align='center'>
                            <div style='display:inline-block; padding:10px 20px; font-size:16px; font-weight:bold; color:#ffffff; background-color:#007BFF; border-radius:6px;'>
                                $15 / month
                            </div>
                        </td>
                    </tr>

                    <tr><td height='30'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#333333;'>
                            Cheers,<br/>
                            <strong>Team Centauri</strong>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
            return (subject, body);
        }

        private (string subject, string body) GetTrialEndingInHoursSubjectAndBody(string firstName, int hours)
        {
            string subject = "Your Centauri trial ends in 48 hours";

            string body = @$"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Trial Ending Soon</title>
</head>
<body style='margin:0; padding:0; font-family: Arial, sans-serif; background-color:#f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4; padding:20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff; padding:30px; border-radius:8px;'>

                    <tr>
                        <td style='font-size:18px; color:#333333;'>
                            Hi {firstName},
                        </td>
                    </tr>

                    <tr><td height='15'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.6;'>
                            Your <strong>Centauri free trial</strong> ends in <strong>{hours} hours</strong>.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:15px; color:#333333; font-weight:bold;'>
                            You still have time to:
                        </td>
                    </tr>

                    <tr><td height='10'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.8;'>
                            • Analyze remaining articles (up to your limit of 5)<br/>
                            • Fix key gaps in structure, authority, and readability<br/>
                            • Improve articles before publishing or updating
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.6;'>
                            If you’ve already tested a few articles, try running one more high-priority piece. Small improvements here can make a big difference.
                        </td>
                    </tr>

                    <tr><td height='30'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#333333;'>
                            Cheers,<br/>
                            <strong>Team Centauri</strong>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
            return (subject, body);
        }

        private (string subject, string body) GetMidTrialSubjectAndBody(string firstName)
        {
            string subject = "You’re halfway through your Centauri trial";

            string body = @$"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Halfway Through Your Trial</title>
</head>
<body style='margin:0; padding:0; font-family: Arial, sans-serif; background-color:#f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4; padding:20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff; padding:30px; border-radius:8px;'>

                    <tr>
                        <td style='font-size:18px; color:#333333;'>
                            Hi {firstName},
                        </td>
                    </tr>

                    <tr><td height='15'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.6;'>
                            You’re now halfway through your <strong>Centauri free trial</strong>.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.6;'>
                            If you haven’t already, this is a great time to run at least <strong>1–2 key articles</strong> through Centauri. Most users see quick wins just by fixing a few gaps in structure and clarity.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.6;'>
                            You still have time to use your remaining analyses—make the most of it.
                        </td>
                    </tr>

                    <tr><td height='30'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#333333;'>
                            Cheers,<br/>
                            <strong>Team Centauri</strong>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
            return (subject, body);
        }
        private (string subject, string body) GetFreeTrialSubjectAndBody(string firstName)
        {
            var fileName = "Centauri Ideal Upload File Sample.docx";
            // Use a local file shipped with the application (e.g. in a Resources or Assets folder).
            // Do NOT attempt to fetch from S3 here.
            var localRelativePath = System.IO.Path.Combine("Resources", fileName);

            string subject = "You're in. Here’s what you can do with Centauri";

            string body = $@"
<!DOCTYPE html>
<html>
<head>
 <meta charset='UTF-8'>
 <title>Welcome to Centauri</title>
</head>
<body style='margin:0; padding:0; font-family: Arial, sans-serif; background-color:#f4f4f4;'>
 <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4; padding:20px;'>
 <tr>
 <td align='center'>
 <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff; padding:30px; border-radius:8px;'>

 <tr>
 <td style='font-size:18px; color:#333333;'>
 Hi {firstName},
 </td>
 </tr>

 <tr><td height='15'></td></tr>

 <tr>
 <td style='font-size:14px; color:#555555; line-height:1.6;'>
 Your email has been verified, and your <strong>Centauri14-day free trial</strong> is now active.
 </td>
 </tr>

 <tr><td height='20'></td></tr>

 <tr>
 <td style='font-size:15px; color:#333333; font-weight:bold;'>
 Here’s what you get in your free trial:
 </td>
 </tr>

 <tr><td height='10'></td></tr>

 <tr>
 <td style='font-size:14px; color:#555555; line-height:1.8;'>
 • Analyze up to 5 articles<br/>
 • Access all scoring and recommendations<br/>
 • Valid for14 days
 </td>
 </tr>

 <tr><td height='20'></td></tr>

 <tr>
 <td style='font-size:14px; color:#555555; line-height:1.6;'>
 Centauri helps you go beyond surface-level SEO and GEO. You’ll see exactly where your article stands across structure, authority, and readability, along with clear suggestions on what to fix.
 </td>
 </tr>


 <tr>
 <td style='font-size:14px; color:#555555; line-height:1.6;'>
 Centauri works best when your content is clearly structured. That’s how it identifies gaps across structure, authority, and readability.
 </td>
 </tr>
 <tr><td height='20'></td></tr>

 <tr>
 <td style='font-size:14px; color:#555555; line-height:1.6;'>
 To help you get accurate results, we’ve created a <strong>sample file showing exactly how your content should be formatted before uploading.</strong>
 </td>
 </tr>

 <tr><td height='20'></td></tr>
 <tr>
 <td style='font-size:14px; color:#555555; line-height:1.6;'>
 The sample file is included with this email as an attachment: <strong>{fileName}</strong>
 </td>
 </tr>
<tr><td height='30'></td></tr>
<tr>
 <td style='font-size:14px; color:#555555; line-height:1.8;'>
 This sample shows:
 <ul style='margin:10px0020px; padding:0;'>
 <li>How to mark Title, H2, H3, and paragraphs</li>
 <li>How to structure lists and tables</li>
 <li>How to clearly define metadata like Meta Title and Description</li>
 </ul>
 </td>
</tr>
<tr><td height='30'></td></tr>

 <tr>
 <td style='font-size:14px; color:#333333;'>
 Cheers,<br/>
 <strong>Team Centauri</strong>
 </td>
 </tr>

 </table>
 </td>
 </tr>
 </table>
</body>
</html>";
            return (subject, body);
        }
        private (string subject, string body) GetSignUpSubjectAndBody(string code)
        {
            string subject = "Your Centauri verification code is here!";

            string body = @$"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Verification Code</title>
</head>
<body style='margin:0; padding:0; font-family: Arial, sans-serif; background-color:#f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4; padding:20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff; padding:30px; border-radius:8px;'>

                    <tr>
                        <td style='font-size:18px; color:#333333;'>
                            Hi,
                        </td>
                    </tr>

                    <tr><td height='15'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555;'>
                            Welcome to <strong>Centauri</strong>.
                        </td>
                    </tr>

                    <tr><td height='10'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555; line-height:1.6;'>
                            Centauri helps you analyze and improve your articles so they perform better across SEO and AI search.
                            Instead of guessing what works, you get clear, actionable feedback on structure, authority, and readability.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555;'>
                            To continue setting up your account, please use the verification code below:
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td align='center'>
                            <div style='display:inline-block; padding:12px 24px; font-size:22px; letter-spacing:4px; font-weight:bold; color:#ffffff; background-color:#28a745; border-radius:6px;'>
                                {code}
                            </div>
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:13px; color:#999999;'>
                            This code will expire in 10 minutes.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:13px; color:#999999;'>
                            If you didn’t request this, you can ignore this email.
                        </td>
                    </tr>

                    <tr><td height='30'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#333333;'>
                            Cheers,<br/>
                            <strong>Team Centauri</strong>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
            return (subject,body);
        }
        private (string,string) GetForgotPassowrdSubjectAndBody(string code)
        {
            string subject = "Reset your Centauri password";

            string body = @$"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Reset Password</title>
</head>
<body style='margin:0; padding:0; font-family: Arial, sans-serif; background-color:#f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4; padding:20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff; padding:30px; border-radius:8px;'>
                    
                    <tr>
                        <td style='font-size:18px; color:#333333;'>
                            Hi,
                        </td>
                    </tr>

                    <tr><td height='15'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555;'>
                            We received a request to reset your Centauri password.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#555555;'>
                            Use the code below to reset your password:
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td align='center'>
                            <div style='display:inline-block; padding:12px 24px; font-size:22px; letter-spacing:4px; font-weight:bold; color:#ffffff; background-color:#007BFF; border-radius:6px;'>
                                {code}
                            </div>
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:13px; color:#999999;'>
                            This code will expire in 10 minutes.
                        </td>
                    </tr>

                    <tr><td height='20'></td></tr>

                    <tr>
                        <td style='font-size:13px; color:#999999;'>
                            If you didn’t request this, you can ignore this email.
                        </td>
                    </tr>

                    <tr><td height='30'></td></tr>

                    <tr>
                        <td style='font-size:14px; color:#333333;'>
                            Cheers,<br/>
                            <strong>Team Centauri</strong>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
            return (subject, body);
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

        public async Task SendEmailWithAttachmentAsync(string toEmail, string subject, string body, string attachmentFilePath)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) throw new ArgumentException("toEmail is required", nameof(toEmail));
            await _emailSender.SendEmailAsync(toEmail, subject, body, attachmentFilePath);
        }
    }
}
