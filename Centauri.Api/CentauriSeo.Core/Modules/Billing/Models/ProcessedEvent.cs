// File: Models/ProcessedEvent.cs
using System;

namespace CentauriSeo.Core.Modules.Billing.Models
{
    /// <summary>
    /// Model used by ProcessedEventRepository for idempotency.
    /// Provided here for completeness (repository also defines a local ProcessedEvent type).
    /// </summary>
    public class ProcessedEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}