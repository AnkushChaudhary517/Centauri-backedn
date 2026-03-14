// File: Repositories/ArticleRepository.cs
using System;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using CentauriSeo.Core.Modules.Billing.Models;

namespace CentauriSeo.Core.Modules.Billing.Repositories
{
    /// <summary>
    /// Stores processed article records to prevent double charging on re-analysis.
    /// Primary key strategy: composite key (UserId + ArticleId) stored as the Hash key Id.
    /// </summary>
    public class ArticleRepository
    {
        private readonly IDynamoDBContext _db;

        public ArticleRepository(IAmazonDynamoDB dynamoDb)
        {
            _db = new DynamoDBContext(dynamoDb);
        }

        /// <summary>
        /// Retrieves article record by userId and articleId.
        /// </summary>
        public async Task<ArticleRecord?> GetByArticleIdAsync(string userId, string articleId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(articleId)) return null;
            var key = ComposeKey(userId, articleId);
            return await _db.LoadAsync<ArticleRecord>(key);
        }

        public async Task SaveAsync(ArticleRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.Id)) record.Id = ComposeKey(record.UserId, record.ArticleId);
            await _db.SaveAsync(record);
        }

        private static string ComposeKey(string userId, string articleId) => $"{userId}#{articleId}";
    }
}