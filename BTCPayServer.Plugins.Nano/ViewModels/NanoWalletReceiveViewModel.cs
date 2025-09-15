
namespace BTCPayServer.Plugins.Nano.ViewModels
{
    public class NanoWalletReceiveViewModel
    {
        public string? CryptoImage { get; set; }
        public string? CryptoCode { get; set; }
        public string? Address { get; set; }
        public string? PaymentLink { get; set; }
        public string? ReturnUrl { get; set; }
        public string[]? SelectedLabels { get; set; }
    }
}