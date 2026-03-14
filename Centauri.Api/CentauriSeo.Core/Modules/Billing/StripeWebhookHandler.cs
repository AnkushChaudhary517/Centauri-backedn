//// File: Modules/Billing/StripeWebhookHandler.cs
//using System;
//using System.Threading.Tasks;
//using Microsoft.Extensions.Logging;
//using Stripe;
//using CentauriSeo.Core.Modules.Billing.Repositories;
//using CentauriSeo.Core.Modules.Billing.Models;

//namespace CentauriSeo.Core.Modules.Billing
//{
//    /// <summary>
//    /// Processes Stripe events in an idempotent way. Uses ProcessedEventRepository to avoid double-processing.
//    /// Business rules applied:
//    /// - checkout.session.completed => topup or subscription depending on session mode & metadata
//    /// - invoice.paid => grant monthly credits for active subscription cycles
//    /// - invoice.payment_failed => mark subscription or notify (minimal here)
//    /// - customer.subscription.deleted => mark subscription cancelled and optionally stop monthly credits
//    /// </summary>
//    public class StripeWebhookHandler
//    {
//        private readonly SubscriptionRepository _subscriptionRepo;
//        private readonly CreditLedgerRepository _ledgerRepo;
//        private readonly ProcessedEventRepository _processedEventRepo;
//        private readonly CreditService _creditService;
//        private readonly ArticleUsageService _articleUsageService;
//        private readonly ILogger _logger;

//        public StripeWebhookHandler(
//            SubscriptionRepository subscriptionRepo,
//            CreditLedgerRepository ledgerRepo,
//            ProcessedEventRepository processedEventRepo,
//            CreditService creditService,
//            ArticleUsageService articleUsageService,
//            ILogger logger)
//        {
//            _subscriptionRepo = subscriptionRepo;
//            _ledgerRepo = ledgerRepo;
//            _processedEventRepo = processedEventRepo;
//            _creditService = creditService;
//            _articleUsageService = articleUsageService;
//            _logger = logger;
//        }

//        public async Task HandleEventAsync(Event stripeEvent)
//        {
//            if (stripeEvent == null) throw new ArgumentNullException(nameof(stripeEvent));

//            // Idempotency guard
//            var eventId = stripeEvent.Id;
//            if (await _processedEventRepo.ExistsAsync(eventId))
//            {
//                _logger.LogInformation("Stripe event {EventId} already processed, skipping.", eventId);
//                return;
//            }

//            try
//            {
//                switch (stripeEvent.Type)
//                {
//                    case "checkout.session.completed":
//                        await HandleCheckoutSessionCompletedAsync(stripeEvent);
//                        break;

//                    case "invoice.paid":
//                        await HandleInvoicePaidAsync(stripeEvent);
//                        break;

//                    case "invoice.payment_failed":
//                        await HandleInvoicePaymentFailedAsync(stripeEvent);
//                        break;

//                    case "customer.subscription.deleted":
//                        await HandleSubscriptionDeletedAsync(stripeEvent);
//                        break;

//                    default:
//                        _logger.LogInformation("Unhandled stripe event type: {Type}", stripeEvent.Type);
//                        break;
//                }

//                // Mark event processed
//                await _processedEventRepo.SaveAsync(new ProcessedEvent { Id = eventId, ProcessedAt = DateTime.UtcNow });
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error handling stripe event {EventId} type {Type}", eventId, stripeEvent.Type);
//                throw;
//            }
//        }

//        private async Task HandleCheckoutSessionCompletedAsync(Event evt)
//        {
//            var session = evt.Data.Object as Stripe.Checkout.Session;
//            if (session == null) return;

//            // metadata should contain "userId" and optionally "quantity"
//            session.Metadata.TryGetValue("userId", out var userId);

//            // If payment was for a subscription, subscription id will be present on session
//            if (!string.IsNullOrWhiteSpace(session.SubscriptionId))
//            {
//                // subscription created via Checkout: record subscription and grant initial monthly credits
//                var subRecord = new SubscriptionRecord
//                {
//                    UserId = userId ?? string.Empty,
//                    StripeSubscriptionId = session.SubscriptionId,
//                    StripeCustomerId = session.CustomerId,
//                    Status = SubscriptionStatus.Active,
//                    StartDate = DateTime.UtcNow,
//                    CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1) // best-effort; accurate value can be pulled from Stripe API if needed
//                };

//                await _subscriptionRepo.SaveAsync(subRecord);

//                // Grant monthly credits (10) for new subscription period
//                await _creditService.AddCreditsAsync(userId, 10, CreditType.Monthly, expiresAt: DateTime.UtcNow.AddMonths(1), reference: $"subscription:{subRecord.StripeSubscriptionId}");
//            }
//            else
//            {
//                // One-time payment (topup) => quantity should be present in metadata
//                if (session.Metadata.TryGetValue("quantity", out var q) && int.TryParse(q, out var quantity) && !string.IsNullOrWhiteSpace(userId))
//                {
//                    // Grant top-up credits
//                    await _creditService.AddCreditsAsync(userId, quantity, CreditType.Topup, expiresAt: null, reference: $"topup:{session.Id}");
//                }
//            }
//        }

//        private async Task HandleInvoicePaidAsync(Event evt)
//        {
//            var invoice = evt.Data.Object as Invoice;
//            if (invoice == null) return;

//            // invoice for subscription renewal: grant monthly credits when invoice is paid
//            var subscriptionId = invoice.SubscriptionId;
//            if (string.IsNullOrWhiteSpace(subscriptionId)) return;

//            // Find subscription record by stripe subscription id
//            var subscription = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(subscriptionId);
//            if (subscription == null)
//            {
//                _logger.LogWarning("Invoice.paid for unknown subscription {SubscriptionId}", subscriptionId);
//                return;
//            }

//            // Grant monthly credits for the billing period (10 credits)
//            await _creditService.AddCreditsAsync(subscription.UserId, 10, CreditType.Monthly, expiresAt: DateTime.UtcNow.AddMonths(1), reference: $"invoice:{invoice.Id}");
//        }

//        private async Task HandleInvoicePaymentFailedAsync(Event evt)
//        {
//            var invoice = evt.Data.Object as Invoice;
//            if (invoice == null) return;

//            var subscriptionId = invoice.SubscriptionId;
//            if (string.IsNullOrWhiteSpace(subscriptionId)) return;

//            var subscription = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(subscriptionId);
//            if (subscription == null) return;

//            // Mark subscription as past_due / notify - for now update status in repo
//            subscription.Status = SubscriptionStatus.PastDue;
//            await _subscriptionRepo.UpdateAsync(subscription);
//            _logger.LogInformation("Marked subscription {SubscriptionId} as PastDue", subscriptionId);
//        }

//        private async Task HandleSubscriptionDeletedAsync(Event evt)
//        {
//            var subscription = evt.Data.Object as Stripe.Subscription;
//            if (subscription == null) return;

//            var subRecord = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(subscription.Id);
//            if (subRecord == null) return;

//            subRecord.Status = SubscriptionStatus.Cancelled;
//            subRecord.CancelledAt = DateTime.UtcNow;
//            await _subscriptionRepo.UpdateAsync(subRecord);

//            // Invalidate monthly credits by setting their expiry to now (soft approach)
//            var ledger = await _ledgerRepo.GetLedgerByUserAsync(subRecord.UserId);
//            foreach (var item in ledger)
//            {
//                if (item.Type == CreditType.Monthly && (!item.ExpiresAt.HasValue || item.ExpiresAt.Value > DateTime.UtcNow))
//                {
//                    item.ExpiresAt = DateTime.UtcNow;
//                    await _ledgerRepo.UpdateLedgerItemAsync(item);
//                }
//            }

//            _logger.LogInformation("Processed subscription deletion for user {UserId}", subRecord.UserId);
//        }
//    }
//}