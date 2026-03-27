using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iTextSharp.text.pdf;

namespace CentauriSeo.Core.Invoice;

public interface IPdfService
{
    byte[] GenerateInvoicePdf(Invoice invoice);
}

public class PdfService : IPdfService
{
    public byte[] GenerateInvoicePdf(Invoice invoice)
    {
        using var stream = new MemoryStream();
        var writer = new iText.Kernel.Pdf.PdfWriter(stream);
        var pdf = new iText.Kernel.Pdf.PdfDocument(writer);
        var doc = new Document(pdf);

        // Title
        doc.Add(new Paragraph("INVOICE").SimulateBold().SetFontSize(20));

        // Customer Info
        doc.Add(new Paragraph($"Customer: {invoice.CustomerName}"));
        doc.Add(new Paragraph($"Email: {invoice.CustomerEmail}"));
        doc.Add(new Paragraph($"Invoice No: {invoice.InvoiceNumber}"));
        doc.Add(new Paragraph($"Date: {invoice.CreatedAt:dd-MM-yyyy}"));

        // Table
        var table = new Table(4);

        table.AddHeaderCell("Plan");
        table.AddHeaderCell("Billing");
        table.AddHeaderCell("Price");
        table.AddHeaderCell("Total");

        table.AddCell(invoice.PlanName);
        table.AddCell(invoice.BillingCycle);
        table.AddCell(invoice.SubTotal.ToString("0.00"));
        table.AddCell(invoice.TotalAmount.ToString("0.00"));

        doc.Add(table);

        // Pricing breakdown
        doc.Add(new Paragraph($"Subtotal: ₹{invoice.SubTotal}"));
        doc.Add(new Paragraph($"GST (18%): ₹{invoice.GstAmount}"));
        doc.Add(new Paragraph($"Total: ₹{invoice.TotalAmount}").SimulateBold());

        // Payment
        doc.Add(new Paragraph($"Payment ID: {invoice.PaymentId}"));
        doc.Add(new Paragraph($"Order ID: {invoice.OrderId}"));

        doc.Close();

        return stream.ToArray();
    }
}
