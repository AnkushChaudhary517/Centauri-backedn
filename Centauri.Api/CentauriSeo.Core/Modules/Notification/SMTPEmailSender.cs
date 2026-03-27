using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Linq;
// Services/SmtpEmailSender.cs
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
namespace CentauriSeo.Core.Modules.Notification
{


    public class SmtpEmailSender : IEmailSender
    {
        private readonly string _smtpHost = "smtp.sendgrid.net";
        private readonly int _smtpPort = 465;
        private readonly string _username = "apikey";//"contact@thedigna.com";
        private readonly string _password = "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";//"rrqewydeeugqnkqw";//"sjld nmrn ipzc vowb";

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var smtpClient = new SmtpClient(_smtpHost)
                {
                    Port = 587,
                    Credentials = new NetworkCredential(
               _username,                 // fixed
               _password  // your API key
                    ),
                    EnableSsl = true
                };

                var message = new MailMessage
                {
                    From = new MailAddress("contact@getcentauri.com", "Get Centauri"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                message.To.Add(toEmail);

                await smtpClient.SendMailAsync(message);
            }
            catch (Exception ex)
            {
            }
        }
    }
}
