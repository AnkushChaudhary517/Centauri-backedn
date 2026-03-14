// File: Repositories/ProcessedEventRepository.cs
using System;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;

namespace CentauriSeo.Core.Modules.Billing.Repositories
{
    /// <summary>
    /// Tracks processed webhook/event ids to ensure idempotency.
    /// </summary>
    public class ProcessedEventRepository
    {
        private readonly IDynamoDBContext _db;

        public ProcessedEventRepository(IAmazonDynamoDB dynamoDb)
        {
            _db = new DynamoDBContext(dynamoDb);
        }

        public async Task<bool> ExistsAsync(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return false;
            var item = await _db.LoadAsync<ProcessedEvent>(eventId);
            return item != null;
        }

        public async Task SaveAsync(ProcessedEvent item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString();
            await _db.SaveAsync(item);
        }
    }

    // simple data model for processed events
    public class ProcessedEvent
    {
        public string Id { get; set; } = string.Empty; // event id as PK
        public DateTime ProcessedAt { get; set; }
    }
}