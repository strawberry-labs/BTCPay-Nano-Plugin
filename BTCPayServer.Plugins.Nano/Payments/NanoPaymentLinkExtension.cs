using System.Globalization;

using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoPaymentLinkExtension : IPaymentLinkExtension
    {
        private readonly NanoLikeSpecificBtcPayNetwork _network;
        public NanoPaymentLinkExtension(PaymentMethodId paymentMethodId, NanoLikeSpecificBtcPayNetwork network)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
        }

        public PaymentMethodId PaymentMethodId { get; }

        public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        {
            var due = prompt.Calculate().Due; // decimal XNO
                                              // If no amount is due, return just the address link.
            if (due <= 0m)
                return $"{_network.UriScheme}:{prompt.Destination}";

            var raw = XnoToRawString(due);
            return $"{_network.UriScheme}:{prompt.Destination}?amount={raw}";
        }

        // Converts XNO decimal to a raw string (scaled by 10^30) without overflow.
        private static string XnoToRawString(decimal xno)
        {
            // Up to 30 fractional digits; truncate beyond that (don’t round up).
            var s = xno.ToString("0.##############################", CultureInfo.InvariantCulture);
            var parts = s.Split('.');
            var intPart = parts[0].TrimStart('+');
            var fracPart = parts.Length > 1 ? parts[1] : string.Empty;

            if (fracPart.Length > 30)
                fracPart = fracPart.Substring(0, 30);

            var combined = (intPart + fracPart.PadRight(30, '0')).TrimStart('0');
            return string.IsNullOrEmpty(combined) ? "0" : combined;
        }
    }
}