namespace CentauriSeo.Core.Invoice;

public interface IInvoiceService
{
    Invoice GenerateInvoice(CreateInvoiceRequest request);
}

public class InvoiceService : IInvoiceService
{
    public Invoice GenerateInvoice(CreateInvoiceRequest request)
    {
        const decimal GST_RATE = 0.18m;

        var subTotal = request.Amount;
        var gstAmount = Math.Round(subTotal * GST_RATE, 2);
        var total = subTotal + gstAmount;

        return new Invoice
        {
            InvoiceNumber = GenerateInvoiceNumber(),

            UserId = request.UserId,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,

            PlanId = request.PlanId,
            PlanName = request.PlanName,
            BillingCycle = request.BillingCycle,

            SubTotal = subTotal,
            GstRate = GST_RATE,
            GstAmount = gstAmount,
            TotalAmount = total,

            PaymentId = request.RazorpayPaymentId,
            OrderId = request.RazorpayOrderId
        };
    }
    private string GenerateInvoiceNumber()
    {
        return $"INV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
    }
}
