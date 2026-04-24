using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Modules.Notification
{
    // Services/IEmailSender.cs
    public interface IEmailSender
    {
        Task SendEmailAsync(string to, string subject, string body, string? attachmentFilePath = null);
    }
}
