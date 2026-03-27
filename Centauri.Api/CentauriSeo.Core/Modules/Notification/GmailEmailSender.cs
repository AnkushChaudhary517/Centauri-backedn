using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net.Mail;
using System.Threading.Tasks;
namespace CentauriSeo.Core.Modules.Notification
{
    public class EmailService
    {
        private readonly string _apiKey = "YOUR_SENDGRID_API_KEY";

        public async Task SendEmailAsync(string toEmail, string subject, string body)
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

            var response = await client.SendEmailAsync(msg);
        }
    }
}
