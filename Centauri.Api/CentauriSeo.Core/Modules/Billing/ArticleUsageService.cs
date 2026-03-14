// File: Modules/Billing/ArticleUsageService.cs
using System;
using System.Threading.Tasks;
using CentauriSeo.Core.Modules.Billing.Repositories;
using CentauriSeo.Core.Modules.Billing.Models;

namespace CentauriSeo.Core.Modules.Billing
{
    /// <summary>
    /// Handles article-unit calculation and recording article usages.
    /// Ensures re-analysis of the same articleId is idempotent (does not consume credits again).
    /// </summary>
    public class ArticleUsageService
    {
        private readonly ArticleRepository _articleRepo;
        private readonly CreditService _creditService;

        public ArticleUsageService(ArticleRepository articleRepo, CreditService creditService)
        {
            _articleRepo = articleRepo ?? throw new ArgumentNullException(nameof(articleRepo));
            _creditService = creditService ?? throw new ArgumentNullException(nameof(creditService));
        }

        /// <summary>
        /// Calculates how many credit units the article consumes based on word count:
        /// <=3000 => 1
        /// 3001-6000 => 2
        /// >6000 => ceil(wordcount / 3000)
        /// </summary>
        public int CalculateArticleUnits(int wordCount)
        {
            if (wordCount <= 0) return 1;
            if (wordCount <= 3000) return 1;
            if (wordCount <= 6000) return 2;
            return (int)Math.Ceiling(wordCount / 3000.0);
        }

        /// <summary>
        /// Validates whether the user can analyze the article.
        /// - If user has active subscription -> check available credits
        /// - If user is on trial -> allow until trial credits consumed and trial expiry
        /// - After trial expiry and no subscription -> block
        /// This method expects upstream code to enforce subscription presence for topups if required.
        /// </summary>
        public async Task<bool> CanAnalyzeArticleAsync(string userId, SubscriptionRecord subscription)
        {
            // If user has an active subscription (status = Active) allow if credits exist
            if (subscription != null && subscription.IsActive)
            {
                var available = await _creditService.GetAvailableCreditsAsync(userId);
                return available > 0;
            }

            // No active subscription -> check trial credits (trial ledger entries)
            var trialCredits = await _creditService.GetCreditsByTypeAsync(userId, CreditType.Trial);
            if (trialCredits > 0) return true;

            // Trial expired or exhausted => blocked
            return false;
        }

        /// <summary>
        /// Record article usage. If article already recorded for this user/articleId -> do nothing.
        /// Otherwise consumes credits and stores ArticleRecord.
        /// </summary>
        public async Task RecordArticleUsageAsync(string userId, string articleId, int wordCount)
        {
            // If already processed, do not consume credits (idempotency)
            var existing = await _articleRepo.GetByArticleIdAsync(userId, articleId);
            if (existing != null) return;

            var units = CalculateArticleUnits(wordCount);

            // Consume credits (may throw if insufficient)
            await _creditService.ConsumeCreditsAsync(userId, units, articleId);

            var record = new ArticleRecord
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ArticleId = articleId,
                WordCount = wordCount,
                UnitsConsumed = units,
                ProcessedAt = DateTime.UtcNow
            };

            await _articleRepo.SaveAsync(record);
        }
    }
}