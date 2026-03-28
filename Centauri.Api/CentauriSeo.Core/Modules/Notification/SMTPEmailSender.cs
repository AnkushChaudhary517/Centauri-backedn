using CentauriSeo.Core.Models.Utilities;
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
        private readonly string _password = "U0cuOTFqUFM0eVpTV0NEZlFqSUswY1RPUS5sSmdtWmpLRlNqQ21GRTVfa3J4bC1wTDk3SllpaW4yT1ZMOXY5TnNrZElj";//"rrqewydeeugqnkqw";//"sjld nmrn ipzc vowb";

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var p = _password.DecodeBase64();
                var smtpClient = new SmtpClient(_smtpHost)
                {
                    Port = 587,
                    Credentials = new NetworkCredential(
               _username,                 // fixed
               p  // your API key
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
                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                await smtpClient.SendMailAsync(message);
            }
            catch (Exception ex)
            {
            }
        }
    }
}
