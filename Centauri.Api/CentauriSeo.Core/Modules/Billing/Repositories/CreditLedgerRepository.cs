// File: Repositories/CreditLedgerRepository.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using CentauriSeo.Core.Modules.Billing.Models;

namespace CentauriSeo.Core.Modules.Billing.Repositories
{
    /// <summary>
    /// Manages credit ledger items in DynamoDB.
    /// Provides simple operations used by CreditService.
    /// </summary>
    public class CreditLedgerRepository
    {
        private readonly IDynamoDBContext _db;

        public CreditLedgerRepository(IAmazonDynamoDB dynamoDb)
        {
            _db = new DynamoDBContext(dynamoDb);
        }

        public async Task<IEnumerable<CreditLedgerItem>> GetLedgerByUserAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return Enumerable.Empty<CreditLedgerItem>();

            // Query by userId (assumes UserId is the hash key)
            var conditions = new List<ScanCondition> { new ScanCondition("UserId", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, userId) };
            var search = _db.ScanAsync<CreditLedgerItem>(conditions);
            var all = new List<CreditLedgerItem>();
            do
            {
                var batch = await search.GetNextSetAsync();
                all.AddRange(batch);
            } while (!search.IsDone);

            return all;
        }

        public async Task AddLedgerItemAsync(CreditLedgerItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString();
            await _db.SaveAsync(item);
        }

        public async Task UpdateLedgerItemAsync(CreditLedgerItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            await _db.SaveAsync(item);
        }
    }
}