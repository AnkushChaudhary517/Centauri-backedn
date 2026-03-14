using System;

namespace CentauriSeo.Core.Modules.Billing.Models
{
    /// <summary>
    /// Simple DTO returned by billing APIs summarizing available credits for a user.
    /// Kept in core billing models so it can be consumed by services and controllers.
    /// </summary>
    public sealed class UsageSummary
    {
        public int AvailableCredits { get; init; }
        public int TrialCreditsRemaining { get; init; }
        public int MonthlyCredits { get; init; }
        public int TopupCredits { get; init; }

        public UsageSummary() { }

        public UsageSummary(int availableCredits, int trialCreditsRemaining, int monthlyCredits, int topupCredits)
        {
            AvailableCredits = availableCredits;
            TrialCreditsRemaining = trialCreditsRemaining;
            MonthlyCredits = monthlyCredits;
            TopupCredits = topupCredits;
        }
    }
}