namespace CentauriSeo.Core.Modules.Billing.Models
{
    /// <summary>
    /// Represents a Stripe Checkout session result returned to the frontend.
    /// Placed in the Core billing models so services and controllers share the same type.
    /// </summary>
    public sealed class CheckoutResponse
    {
        public string SessionId { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;

        public CheckoutResponse() { }

        public CheckoutResponse(string sessionId, string url)
        {
            SessionId = sessionId ?? string.Empty;
            Url = url ?? string.Empty;
        }
    }
}