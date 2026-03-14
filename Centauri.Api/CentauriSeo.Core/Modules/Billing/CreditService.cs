// File: Modules/Billing/CreditService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CentauriSeo.Core.Modules.Billing.Models;
using CentauriSeo.Core.Modules.Billing.Repositories;

namespace CentauriSeo.Core.Modules.Billing
{
    /// <summary>
    /// Manages credit ledger operations: adding credits, consuming, and queries.
    /// Credits are stored as ledger items and aggregated to compute availability.
    /// Expirations are respected when computing available credits.
    /// </summary>
    public class CreditService
    {
        private readonly CreditLedgerRepository _ledgerRepo;

        public CreditService(CreditLedgerRepository ledgerRepo)
        {
            _ledgerRepo = ledgerRepo ?? throw new ArgumentNullException(nameof(ledgerRepo));
        }

        /// <summary>
        /// Adds credits for a user. type influences expiration rules:
        /// - trial: expires after 14 days (business rules external may set precise expiry)
        /// - monthly: expires at billing cycle end (we set ExpiresAt = now + 1 month)
        /// - topup: no expiry by default
        /// </summary>
        public async Task AddCreditsAsync(string userId, int amount, CreditType type, DateTime? expiresAt = null, string? reference = null)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            var now = DateTime.UtcNow;

            var item = new CreditLedgerItem
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Amount = amount,
                Type = type,
                CreatedAt = now,
                Reference = reference,
                ExpiresAt = expiresAt ?? (type == CreditType.Trial ? now.AddDays(14) : type == CreditType.Monthly ? now.AddMonths(1) : (DateTime?)null)
            };

            await _ledgerRepo.AddLedgerItemAsync(item);
        }

        /// <summary>
        /// Consume credits for a user. This creates a negative ledger entry of type Usage.
        /// The method checks availability first and throws if insufficient credits.
        /// </summary>
        public async Task ConsumeCreditsAsync(string userId, int amount, string? referenceArticleId = null)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

            var available = await GetAvailableCreditsAsync(userId);
            if (available < amount) throw new InvalidOperationException("Insufficient credits.");

            var usageItem = new CreditLedgerItem
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Amount = -amount,
                Type = CreditType.Usage,
                CreatedAt = DateTime.UtcNow,
                Reference = referenceArticleId
            };

            await _ledgerRepo.AddLedgerItemAsync(usageItem);
        }

        /// <summary>
        /// Computes currently available credits for a user by summing non-expired ledger items.
        /// </summary>
        public async Task<int> GetAvailableCreditsAsync(string userId)
        {
            var ledger = await _ledgerRepo.GetLedgerByUserAsync(userId);
            var now = DateTime.UtcNow;

            // Sum all amounts where not expired (ExpiresAt == null || ExpiresAt > now)
            var available = ledger
                .Where(i => !i.ExpiresAt.HasValue || i.ExpiresAt.Value > now)
                .Sum(i => i.Amount);

            // Never return negative available credits
            return Math.Max(0, available);
        }

        /// <summary>
        /// Helper to fetch credits aggregated by a specific type (non-expired).
        /// </summary>
        public async Task<int> GetCreditsByTypeAsync(string userId, CreditType type)
        {
            var ledger = await _ledgerRepo.GetLedgerByUserAsync(userId);
            var now = DateTime.UtcNow;
            return Math.Max(0, ledger
                .Where(i => i.Type == type && (!i.ExpiresAt.HasValue || i.ExpiresAt.Value > now))
                .Sum(i => i.Amount));
        }
    }
}