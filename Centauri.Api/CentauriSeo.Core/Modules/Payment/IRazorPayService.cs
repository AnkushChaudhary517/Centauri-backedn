using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Modules.Payment
{
    public interface IRazorpayService
    {
        string CreateOrder(Dictionary<string,object> options);
        string GetKeyId();
        bool VerifyPayment(string razorpayOrderId, string razorpayPaymentId, string razorpaySignature);
    }
}
