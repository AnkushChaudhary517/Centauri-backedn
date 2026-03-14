// File: Repositories/SubscriptionRepository.cs
using System;
using System.Threading.Tasks;
using Amazon.DynamoDBv2; // assume AWS SDK available
using Amazon.DynamoDBv2.DataModel;
using CentauriSeo.Core.Modules.Billing.Models;

namespace CentauriSeo.Core.Modules.Billing.Repositories
{
    /// <summary>
    /// Repository for Subscription records backed by DynamoDB.
    /// Methods are async and lightweight; assume an injected IAmazonDynamoDB client elsewhere.
    /// </summary>
    public class SubscriptionRepository
    {
        private readonly IDynamoDBContext _db;

        public SubscriptionRepository(IAmazonDynamoDB dynamoDb)
        {
            _db = new DynamoDBContext(dynamoDb);
        }

        public async Task<SubscriptionRecord?> GetByUserIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return null;
            return await _db.LoadAsync<SubscriptionRecord>(userId);
        }

        public async Task<SubscriptionRecord?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId)
        {
            if (string.IsNullOrWhiteSpace(stripeSubscriptionId)) return null;
            // DynamoDB access patterns vary; here we assume subscription id is the hash key in a GSI.
            // For simplicity fallback to scan (not ideal for production).
            var conditions = new[] { new ScanCondition("StripeSubscriptionId", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, stripeSubscriptionId) };
            var search = _db.ScanAsync<SubscriptionRecord>(conditions);
            var list = await search.GetNextSetAsync();
            return list?.Count > 0 ? list[0] : null;
        }

        public async Task SaveAsync(SubscriptionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            // Use userId as primary key for quick lookup
            record.UserId = record.UserId ?? Guid.NewGuid().ToString();
            await _db.SaveAsync(record);
        }

        public async Task UpdateAsync(SubscriptionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            await _db.SaveAsync(record);
        }
    }
}