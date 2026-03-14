using System;

namespace CentauriSeo.Core.Modules.Billing.Models
{
    /// <summary>
    /// Local project exception type used to surface Stripe-related verification/errors
    /// to callers that do not reference the Stripe.NET package directly.
    /// This intentionally derives from <see cref="Exception"/> so it can be caught
    /// interchangeably where the project expects a Stripe-related exception.
    /// </summary>
    public class StripeException : Exception
    {
        public string? StripeErrorCode { get; }

        public StripeException() { }

        public StripeException(string message) : base(message) { }

        public StripeException(string message, Exception inner) : base(message, inner) { }

        public StripeException(string message, string? stripeErrorCode) : base(message)
        {
            StripeErrorCode = stripeErrorCode;
        }
    }
}