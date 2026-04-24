using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net.Mail;
using System.Threading.Tasks;
using System.IO;

namespace CentauriSeo.Core.Modules.Notification
{
    public class EmailService : IEmailSender
    {
        private readonly string _apiKey = "YOUR_SENDGRID_API_KEY";

        public async Task SendEmailAsync(string toEmail, string subject, string body, string? attachmentFilePath = null)
        {
            var client = new SendGridClient(_apiKey);

            var from = new EmailAddress("your@email.com", "Your App");
            var to = new EmailAddress(toEmail);

            var msg = MailHelper.CreateSingleEmail(
                from,
                to,
                subject,
                body,
                body
            );

            if (!string.IsNullOrWhiteSpace(attachmentFilePath) && File.Exists(attachmentFilePath))
            {
                var bytes = await File.ReadAllBytesAsync(attachmentFilePath);
                var base64 = System.Convert.ToBase64String(bytes);
                msg.AddAttachment(Path.GetFileName(attachmentFilePath), base64);
            }

            var response = await client.SendEmailAsync(msg);
        }
    }
}
