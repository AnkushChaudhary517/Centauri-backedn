using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Modules.Notification
{
    // Repositories/IEmailVerificationRepository.cs
    public interface IEmailVerificationRepository
    {
        Task SaveCodeAsync(EmailVerificationCode code);

        Task<EmailVerificationCode> GetCodeAsync(string email);

        Task MarkUsedAsync(string email);
    }
}
