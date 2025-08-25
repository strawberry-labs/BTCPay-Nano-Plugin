namespace BTCPayServer.Plugins.Nano;

public class NanoLikeSpecificBtcPayNetwork : BTCPayNetworkBase
{
    public int MaxTrackedConfirmation = 10;
    public string UriScheme { get; set; }
}