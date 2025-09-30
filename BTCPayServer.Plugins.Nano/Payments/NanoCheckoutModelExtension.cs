using System.Collections.Generic;
using System.Linq;
using System;

using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoCheckoutModelExtension : ICheckoutModelExtension
    {
        private readonly BTCPayNetworkBase _network;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly IPaymentLinkExtension paymentLinkExtension;

        public NanoCheckoutModelExtension(
            PaymentMethodId paymentMethodId,
            IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
            BTCPayNetworkBase network,
            PaymentMethodHandlerDictionary handlers)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
            _handlers = handlers;
            paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
        }
        public PaymentMethodId PaymentMethodId { get; }

        public string Image => _network.CryptoImagePath;
        public string Badge => "";

        public void ModifyCheckoutModel(CheckoutModelContext context)
        {
            Console.WriteLine("HERE IMAGE " + Image);
            if (context is not { Handler: NanoLikePaymentMethodHandler handler })
            {
                return;
            }
            context.Model.CheckoutBodyComponentName = BitcoinCheckoutModelExtension.CheckoutBodyComponentName;
            var details = context.InvoiceEntity.GetPayments(true)
                    .Select(p => p.GetDetails<NanoLikePaymentData>(handler))
                    .Where(p => p is not null)
                    .FirstOrDefault();
            if (details is not null)
            {
                // context.Model.ReceivedConfirmations = details.Confirmation;
                // TODO: Come back and check once u implement the Nano Listener
                // context.Model.RequiredConfirmations = (int)NanoListener.ConfirmationsRequired(details, context.InvoiceEntity.SpeedPolicy);
            }

            context.Model.InvoiceBitcoinUrl = paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
        }
    }
}