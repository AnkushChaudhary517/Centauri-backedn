// File: Models/SubscriptionRecord.cs
using System;

namespace CentauriSeo.Core.Modules.Billing.Models
{
    public enum SubscriptionStatus
    {
        None,
        Active,
        PastDue,
        Cancelled
    }

    /// <summary>
    /// Represents a user's subscription state persisted in DynamoDB.
    /// Primary key is UserId for quick retrieval.
    /// </summary>
    public class SubscriptionRecord
    {
        public string UserId { get; set; } = Guid.NewGuid().ToString();
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.None;
        public DateTime? StartDate { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public DateTime? CancelledAt { get; set; }

        public bool IsActive => Status == SubscriptionStatus.Active;
    }
}