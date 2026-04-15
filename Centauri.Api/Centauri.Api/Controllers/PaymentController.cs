using Centauri_Api.Model;
using CentauriSeo.Core.Modules.Payment;
using CentauriSeo.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe.V2;
using System.Security.Claims;

namespace Centauri_Api.Controllers
{

    [ApiController]
    [Route("api/v1/payments")]
    public class PaymentController : ControllerBase
    {
        private readonly IRazorpayService _razorpayService;
        private readonly IDynamoDbService _dynamoDbService;

        public PaymentController(IRazorpayService razorpayService,IDynamoDbService dynamoDbService)
        {
            _razorpayService = razorpayService;
            _dynamoDbService = dynamoDbService;
        }

        // 1️⃣ Create Order
        [HttpPost("razorpay/create-order")]
        public IActionResult CreateOrder([FromBody] RazorPayOrderRequest request)
        {
            Dictionary<string, object> options = new Dictionary<string, object>
        {
            { "amount", request.MonthlyPrice * 100 }, // paise
            { "currency", "USD" },
                //{"plan_name",request.PlanName },
                 //{"billing_cycle",request.BillingCycle },
                  //{"plan",request.Plan },
                  //{"article_analysis_per_month",request.ArticleAnalysesPerMonth },
            { "receipt", Guid.NewGuid().ToString() },
            { "payment_capture", 1 }
        };

            var orderId = _razorpayService.CreateOrder(options);
            return Ok(new RazorPayOrderResponse()
            {
                OrderId = orderId,
                 Amount = request.MonthlyPrice *100,
                  Currency = "USD",
                  KeyId = _razorpayService.GetKeyId()
            });
        }
        [Authorize]
        // 2️⃣ Verify Payment
        [HttpPost("razorpay/verify")]
        public IActionResult VerifyPayment([FromBody] PaymentVerifyRequest req)
        {
            bool isValid = _razorpayService.VerifyPayment(
                req.RazorpayOrderId,
                req.RazorpayPaymentId,
                req.RazorpaySignature
            );

            if (!isValid)
                return BadRequest("Invalid signature");
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var existingUser = _dynamoDbService.GetUserAsync(userId).Result;
            if (existingUser == null)
            {
                return BadRequest(ApiResponseHelper.Error<RegisterResponse>(
                   "INVALID_REQUEST",
                   "User Does not exists",
                   400
               ));
            }
            existingUser.CreditsAdded += 10;
            existingUser.Plan = "starter";
            existingUser.Subscription = "Starter";
            existingUser.CreatedAt = DateTime.UtcNow;
            existingUser.SubscriptionEndsAt = DateTime.UtcNow.AddDays(15);
            existingUser.UpdatedAt = DateTime.UtcNow;
            _dynamoDbService.UpdateUserAsync(existingUser).GetAwaiter().GetResult();
            // TODO: mark subscription active / add credits
            return Ok("Payment verified");
        }
        [HttpPost("razorpay/webhook")]
        public IActionResult Webhook()
        {
            using var reader = new StreamReader(Request.Body);
            var body = reader.ReadToEnd();

            // verify Razorpay webhook signature
            // update DB

            return Ok();
        }
    }
}
