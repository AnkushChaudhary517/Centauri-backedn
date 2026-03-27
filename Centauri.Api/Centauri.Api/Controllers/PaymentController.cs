using Centauri_Api.Model;
using CentauriSeo.Core.Modules.Payment;
using Microsoft.AspNetCore.Mvc;
using Stripe.V2;

namespace Centauri_Api.Controllers
{

    [ApiController]
    [Route("api/v1/payments")]
    public class PaymentController : ControllerBase
    {
        private readonly IRazorpayService _razorpayService;

        public PaymentController(IRazorpayService razorpayService)
        {
            _razorpayService = razorpayService;
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
