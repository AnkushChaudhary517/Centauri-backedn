//// File: Modules/Billing/StripeService.cs
//using System;
//using System.Threading.Tasks;
//using Microsoft.Extensions.Configuration;
//using Stripe;
//using Stripe.Checkout;

//namespace CentauriSeo.Core.Modules.Billing
//{
//    /// <summary>
//    /// Encapsulates calls to Stripe.NET SDK.
//    /// Reads Price IDs and webhook secret from configuration.
//    /// </summary>
//    public class StripeService
//    {
//        private readonly string _priceMonthlyId;
//        private readonly string _priceTopupId;
//        private readonly string _webhookSecret;
//        private readonly CustomerService _customerService;
//        private readonly SessionService _sessionService;

//        public StripeService(IConfiguration configuration)
//        {
//            // read configured values; throw if missing
//            _priceMonthlyId = configuration["Stripe:PriceMonthly"] ?? throw new ArgumentException("Stripe:PriceMonthly missing from configuration");
//            _priceTopupId = configuration["Stripe:PriceTopup"] ?? throw new ArgumentException("Stripe:PriceTopup missing from configuration");
//            _webhookSecret = configuration["Stripe:WebhookSecret"] ?? throw new ArgumentException("Stripe:WebhookSecret missing from configuration");

//            // API key assumed to be configured elsewhere (StripeConfiguration.ApiKey)
//            _customerService = new CustomerService();
//            _sessionService = new SessionService();
//        }

//        /// <summary>
//        /// Create or update a Stripe Customer for the provided email.
//        /// Returns Stripe customer id.
//        /// </summary>
//        public async Task<string> CreateCustomerAsync(string email)
//        {
//            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentNullException(nameof(email));
//            var options = new CustomerCreateOptions { Email = email };
//            var customer = await _customerService.CreateAsync(options);
//            return customer.Id;
//        }

//        /// <summary>
//        /// Creates a subscription checkout session (Stripe Checkout) and returns the Session object.
//        /// Expects metadata.userId to be set by caller.
//        /// </summary>
//        public async Task<Session> CreateSubscriptionCheckoutAsync(string customerId, string userId, string successUrl, string cancelUrl)
//        {
//            var options = new SessionCreateOptions
//            {
//                Mode = "subscription",
//                Customer = customerId,
//                SuccessUrl = successUrl,
//                CancelUrl = cancelUrl,
//                LineItems = new System.Collections.Generic.List<SessionLineItemOptions>
//                {
//                    new SessionLineItemOptions { Price = _priceMonthlyId, Quantity = 1 }
//                },
//                // You can rely on the Stripe price to define trial days, but also include metadata for traceability
//                Metadata = new System.Collections.Generic.Dictionary<string, string> { ["userId"] = userId }
//            };

//            var session = await _sessionService.CreateAsync(options);
//            return session;
//        }

//        /// <summary>
//        /// Creates a top-up (one-time payment) checkout session for given quantity (credits).
//        /// We assume Stripe has a one-time Price for Extra Article Credit; quantity multiplies price.
//        /// metadata includes userId and quantity for webhook reconciliation.
//        /// </summary>
//        public async Task<Session> CreateTopupCheckoutAsync(string customerId, string userId, int quantity, string successUrl, string cancelUrl)
//        {
//            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

//            var options = new SessionCreateOptions
//            {
//                Mode = "payment",
//                Customer = customerId,
//                SuccessUrl = successUrl,
//                CancelUrl = cancelUrl,
//                LineItems = new System.Collections.Generic.List<SessionLineItemOptions>
//                {
//                    new SessionLineItemOptions { Price = _priceTopupId, Quantity = quantity }
//                },
//                Metadata = new System.Collections.Generic.Dictionary<string, string>
//                {
//                    ["userId"] = userId,
//                    ["quantity"] = quantity.ToString()
//                }
//            };

//            var session = await _sessionService.CreateAsync(options);
//            return session;
//        }

//        /// <summary>
//        /// Verifies the webhook signature and returns a Stripe Event.
//        /// Throws StripeException on verification failure.
//        /// </summary>
//        public Event VerifyWebhookSignature(string payload, string signatureHeader)
//        {
//            if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentNullException(nameof(payload));
//            if (string.IsNullOrWhiteSpace(signatureHeader)) throw new ArgumentNullException(nameof(signatureHeader));

//            var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _webhookSecret);
//            return stripeEvent;
//        }
//    }
//}