using Microsoft.Maui.Networking;

namespace SubVora.Mobile.Services;

public class ConnectivityService : IConnectivityService
{
    public bool IsConnected => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
}
