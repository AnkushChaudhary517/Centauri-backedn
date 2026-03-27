namespace CentauriSeo.Core.Invoice;

public class Invoice
{
    public string InvoiceId { get; set; } = Guid.NewGuid().ToString();
    public string InvoiceNumber { get; set; }

    public string UserId { get; set; }

    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }

    public string PlanId { get; set; }
    public string PlanName { get; set; }
    public string BillingCycle { get; set; }

    public decimal SubTotal { get; set; }
    public decimal GstRate { get; set; }
    public decimal GstAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string PaymentId { get; set; }
    public string OrderId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateInvoiceRequest
{
    public string UserId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }

    public string PlanId { get; set; }
    public string PlanName { get; set; }
    public string BillingCycle { get; set; }

    public decimal Amount { get; set; } // base price

    public string RazorpayPaymentId { get; set; }
    public string RazorpayOrderId { get; set; }
}