using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Modules.Notification
{
    // Repositories/EmailVerificationRepository.cs

    using Amazon.DynamoDBv2.DataModel;

    public class EmailVerificationRepository : IEmailVerificationRepository
    {
        private readonly IDynamoDBContext _context;

        public EmailVerificationRepository(IDynamoDBContext context)
        {
            _context = context;
        }

        public async Task SaveCodeAsync(EmailVerificationCode code)
        {
            await _context.SaveAsync(code);
        }

        public async Task<EmailVerificationCode> GetCodeAsync(string email)
        {
            return await _context.LoadAsync<EmailVerificationCode>(email);
        }

        public async Task MarkUsedAsync(string email)
        {
            var record = await _context.LoadAsync<EmailVerificationCode>(email);

            if (record == null)
                return;

            record.IsUsed = true;

            await _context.SaveAsync(record);
        }
    }
}
