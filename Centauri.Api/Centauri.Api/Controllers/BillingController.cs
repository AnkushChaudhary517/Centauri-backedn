//using CentauriSeo.Core.Modules.Billing;
//using CentauriSeo.Core.Modules.Billing.Models;
//using global::CentauriSeo.Core.Modules.Billing;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Logging;
//// File: Modules/Billing/BillingController.cs
//using System;
//using System.Threading.Tasks;

//namespace Centauri_Api.Controllers
//{
//    /// <summary>
//    /// Exposes billing endpoints. Controller delegates all work to services.
//    /// Note: Keep controller thin — no direct Stripe SDK usage here.
//    /// </summary>
//    [ApiController]
//    [Route("billing")]
//    public class BillingController : ControllerBase
//    {
//        private readonly BillingService _billingService;
//        private readonly ILogger<BillingController> _logger;

//        public BillingController(BillingService billingService, ILogger<BillingController> logger)
//        {
//            _billingService = billingService ?? throw new ArgumentNullException(nameof(billingService));
//            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
//        }

//        /// <summary>
//        /// Creates a Stripe Checkout session for subscription purchase.
//        /// Caller must provide userId and email (email used to create/reuse Stripe customer).
//        /// Returns the checkout session URL the frontend will redirect to.
//        /// </summary>
//        [HttpPost("create-subscription-checkout")]
//        public async Task<IActionResult> CreateSubscriptionCheckout([FromBody] CreateSubscriptionRequest request)
//        {
//            if (request == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Email))
//                return BadRequest("userId and email are required.");

//            try
//            {
//                var result = await _billingService.CreateSubscriptionCheckoutAsync(request.UserId, request.Email);
//                return Ok(result);
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error creating subscription checkout for user {UserId}", request.UserId);
//                return StatusCode(500, "Failed to create subscription checkout.");
//            }
//        }

//        /// <summary>
//        /// Creates a Stripe Checkout session for top-up credits.
//        /// Requires quantity (number of credits to purchase).
//        /// </summary>
//        [HttpPost("create-topup-checkout")]
//        public async Task<IActionResult> CreateTopupCheckout([FromBody] CreateTopupRequest request)
//        {
//            if (request == null || string.IsNullOrWhiteSpace(request.UserId) || request.Quantity <= 0)
//                return BadRequest("userId and positive quantity are required.");

//            try
//            {
//                var result = await _billingService.CreateTopupCheckoutAsync(request.UserId, request.Quantity);
//                return Ok(result);
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error creating topup checkout for user {UserId}", request.UserId);
//                return StatusCode(500, "Failed to create topup checkout.");
//            }
//        }

//        /// <summary>
//        /// Stripe webhook endpoint. This accepts raw Stripe payload and signature header.
//        /// The controller forwards to the StripeWebhookHandler which performs idempotent processing.
//        /// </summary>
//        [HttpPost("webhook")]
//        public async Task<IActionResult> HandleWebhook()
//        {
//            var signatureHeader = Request.Headers["Stripe-Signature"].ToString();
//            using var reader = new System.IO.StreamReader(Request.Body);
//            var payload = await reader.ReadToEndAsync();

//            try
//            {
//                await _billingService.HandleStripeWebhookAsync(payload, signatureHeader);
//                return Ok();
//            }
//            catch (StripeException sx)
//            {
//                _logger.LogError(sx, "Stripe verification failed");
//                return BadRequest("Invalid stripe webhook signature.");
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Unhandled error processing stripe webhook");
//                return StatusCode(500);
//            }
//        }

//        /// <summary>
//        /// Returns available credits and basic usage summary for the given user.
//        /// </summary>
//        [HttpGet("usage/{userId}")]
//        public async Task<IActionResult> GetUsage(string userId)
//        {
//            if (string.IsNullOrWhiteSpace(userId)) return BadRequest("userId required.");

//            try
//            {
//                var usage = await _billingService.GetUsageAsync(userId);
//                return Ok(usage);
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error getting usage for user {UserId}", userId);
//                return StatusCode(500, "Failed to get usage.");
//            }
//        }
//    }

//    // DTOs specific to controller requests/responses
//    public record CreateSubscriptionRequest(string UserId, string Email);
//    public record CreateTopupRequest(string UserId, int Quantity);

//    public record CheckoutResponse(string SessionId, string Url);

//    public record UsageSummary(int AvailableCredits, int TrialCreditsRemaining, int MonthlyCredits, int TopupCredits);
//}