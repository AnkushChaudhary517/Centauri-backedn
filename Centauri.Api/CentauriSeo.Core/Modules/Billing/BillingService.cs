//// File: Modules/Billing/BillingService.cs
//using System;
//using System.Threading.Tasks;
//using Microsoft.Extensions.Logging;
//using Stripe.Checkout;
//using CentauriSeo.Core.Modules.Billing.Repositories;
//using CentauriSeo.Core.Modules.Billing.Models;

//namespace CentauriSeo.Core.Modules.Billing
//{
//    /// <summary>
//    /// High-level billing orchestration. Coordinates StripeService, repositories and credit operations.
//    /// Keep business rules here (trial rules, granting monthly credits, topups).
//    /// </summary>
//    public class BillingService
//    {
//        private readonly StripeService _stripe;
//        private readonly SubscriptionRepository _subscriptionRepo;
//        private readonly CreditLedgerRepository _ledgerRepo;
//        private readonly ProcessedEventRepository _processedEventRepo;
//        private readonly CreditService _creditService;
//        private readonly ArticleUsageService _articleUsageService;
//        private readonly ILogger<BillingService> _logger;

//        public BillingService(
//            StripeService stripe,
//            SubscriptionRepository subscriptionRepo,
//            CreditLedgerRepository ledgerRepo,
//            ProcessedEventRepository processedEventRepo,
//            CreditService creditService,
//            ArticleUsageService articleUsageService,
//            ILogger<BillingService> logger)
//        {
//            _stripe = stripe;
//            _subscriptionRepo = subscriptionRepo;
//            _ledgerRepo = ledgerRepo;
//            _processedEventRepo = processedEventRepo;
//            _creditService = creditService;
//            _articleUsageService = articleUsageService;
//            _logger = logger;
//        }

//        /// <summary>
//        /// Create subscription checkout session for user. Returns session id + url.
//        /// NOTE: success/cancel URLs should be provided by caller (frontend).
//        /// </summary>
//        public async Task<CheckoutResponse> CreateSubscriptionCheckoutAsync(string userId, string email, string successUrl = "https://example.com/success", string cancelUrl = "https://example.com/cancel")
//        {
//            // 1) create or reuse Stripe customer
//            var customerId = await _stripe.CreateCustomerAsync(email);

//            // 2) create session
//            var session = await _stripe.CreateSubscriptionCheckoutAsync(customerId, userId, successUrl, cancelUrl);

//            // 3) return the checkout session info (frontend will redirect)
//            return new CheckoutResponse(session.Id, session.Url);
//        }

//        /// <summary>
//        /// Create topup checkout session for user (one-time credit purchase).
//        /// </summary>
//        public async Task<CheckoutResponse> CreateTopupCheckoutAsync(string userId, int quantity, string successUrl = "https://example.com/success", string cancelUrl = "https://example.com/cancel")
//        {
//            // Lookup user's subscription record to obtain customer's Stripe id (if previously created) or create new customer
//            var subscription = await _subscriptionRepo.GetByUserIdAsync(userId);
//            string customerId = subscription?.StripeCustomerId;
//            if (string.IsNullOrWhiteSpace(customerId))
//            {
//                // We need the customer's email to create Stripe customer. In many flows email comes from user profile.
//                // For safety we create anonymous customer; recommend caller provide email for better UX.
//                customerId = await _stripe.CreateCustomerAsync($"user_{userId}@noemail.local");
//                // store minimal subscription record if needed
//                if (subscription == null)
//                {
//                    subscription = new SubscriptionRecord { UserId = userId, StripeCustomerId = customerId, Status = SubscriptionStatus.None };
//                    await _subscriptionRepo.SaveAsync(subscription);
//                }
//                else
//                {
//                    subscription.StripeCustomerId = customerId;
//                    await _subscriptionRepo.UpdateAsync(subscription);
//                }
//            }

//            var session = await _stripe.CreateTopupCheckoutAsync(customerId, userId, quantity, successUrl, cancelUrl);
//            return new CheckoutResponse(session.Id, session.Url);
//        }

//        /// <summary>
//        /// Core webhook handling entrypoint. Delegates to StripeWebhookHandler after verifying signature.
//        /// </summary>
//        public async Task HandleStripeWebhookAsync(string payload, string signatureHeader)
//        {
//            var stripeEvent = _stripe.VerifyWebhookSignature(payload, signatureHeader);

//            var handler = new StripeWebhookHandler(
//                _subscriptionRepo,
//                _ledgerRepo,
//                _processedEventRepo,
//                _creditService,
//                _articleUsageService,
//                _logger);

//            await handler.HandleEventAsync(stripeEvent);
//        }

//        /// <summary>
//        /// Returns usage summary for the user.
//        /// </summary>
//        public async Task<UsageSummary> GetUsageAsync(string userId)
//        {
//            var available = await _creditService.GetAvailableCreditsAsync(userId);
//            var trial = await _creditService.GetCreditsByTypeAsync(userId, CreditType.Trial);
//            var monthly = await _creditService.GetCreditsByTypeAsync(userId, CreditType.Monthly);
//            var topup = await _creditService.GetCreditsByTypeAsync(userId, CreditType.Topup);

//            return new UsageSummary((int)available, (int)trial, (int)monthly, (int)topup);
//        }
//    }
//}