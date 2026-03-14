// File: Models/ArticleRecord.cs
using System;

namespace CentauriSeo.Core.Modules.Billing.Models
{
    /// <summary>
    /// Article processing record used to ensure re-analysis does not consume credits multiple times.
    /// Id is composed as UserId#ArticleId by repository.
    /// </summary>
    public class ArticleRecord
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string ArticleId { get; set; } = string.Empty;
        public int WordCount { get; set; }
        public int UnitsConsumed { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}