using Centauri_Api.Model;
using CentauriSeo.Core.Modules.Payment;
using Microsoft.AspNetCore.Mvc;

namespace Centauri_Api.Controllers
{

    [ApiController]
    [Route("api/payment")]
    public class PaymentController : ControllerBase
    {
        private readonly IRazorpayService _razorpayService;

        public PaymentController(IRazorpayService razorpayService)
        {
            _razorpayService = razorpayService;
        }

        // 1️⃣ Create Order
        [HttpPost("create-order")]
        public IActionResult CreateOrder([FromBody] decimal amount)
        {
            var orderId = _razorpayService.CreateOrder(amount);
            return Ok(new { orderId });
        }

        // 2️⃣ Verify Payment
        [HttpPost("verify")]
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
        [HttpPost("webhook")]
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
