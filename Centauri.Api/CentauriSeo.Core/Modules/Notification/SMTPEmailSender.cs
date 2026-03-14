using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// Services/SmtpEmailSender.cs
using System.Net;
using System.Net.Mail;
namespace CentauriSeo.Core.Modules.Notification
{


    public class SmtpEmailSender : IEmailSender
    {
        private readonly string _smtpHost = "smtp.gmail.com";
        private readonly int _smtpPort = 587;
        private readonly string _username = "contact@thedigna.com";
        private readonly string _password = "sjld nmrn ipzc vowb";

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            var client = new SmtpClient(_smtpHost, _smtpPort)
            {
                Credentials = new NetworkCredential(_username, _password),
                EnableSsl = true
            };

            var message = new MailMessage
            {
                From = new MailAddress(_username),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            message.To.Add(to);

            await client.SendMailAsync(message);
        }
    }
}
