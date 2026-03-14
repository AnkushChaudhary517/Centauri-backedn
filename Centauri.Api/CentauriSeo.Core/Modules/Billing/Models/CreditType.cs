// File: Models/CreditLedgerItem.cs
using System;

namespace CentauriSeo.Core.Modules.Billing.Models
{
    public enum CreditType
    {
        Trial,
        Monthly,
        Topup,
        Usage
    }

    /// <summary>
    /// Ledger item representing a credit add/consume event.
    /// Positive Amount => addition, Negative => consumption (usage).
    /// </summary>
    public class CreditLedgerItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public int Amount { get; set; }
        public CreditType Type { get; set; }
        public string? Reference { get; set; } // e.g. invoice id, subscription id, article id
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
    }
}