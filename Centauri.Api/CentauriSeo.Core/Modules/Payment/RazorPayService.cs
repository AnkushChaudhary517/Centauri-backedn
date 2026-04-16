using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Razorpay.Api;
using Microsoft.Extensions.Configuration;
namespace CentauriSeo.Core.Modules.Payment
{


    public class RazorpayService : IRazorpayService
    {
        private readonly string _key;
        private readonly string _secret;

        public RazorpayService(IConfiguration config)
        {
            _key = Environment.GetEnvironmentVariable("RAZOR_PAY_KEY");
            _secret = Environment.GetEnvironmentVariable("RAZOR_PAY_SECRET");
        }

        public string GetKeyId()
        {
            return _key;
        }
        public string CreateOrder(Dictionary<string, object> options)
        {
            var client = new RazorpayClient(_key, _secret);
            Order order = client.Order.Create(options);
            return order["id"].ToString();
        }

        public bool VerifyPayment(string razorpayOrderId, string razorpayPaymentId, string razorpaySignature)
        {
            string payload = razorpayOrderId + "|" + razorpayPaymentId;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var generatedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

            return generatedSignature == razorpaySignature;
        }
    }
}
