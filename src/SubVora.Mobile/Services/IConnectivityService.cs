namespace SubVora.Mobile.Services;

/// <summary>
/// Whether this device currently has network access.
/// <para>
/// Deliberately a poll, not a subscription. Every view model re-reads <see cref="IsConnected"/> when
/// its screen loads and again after a write fails, because the view models are transient while this
/// service is a singleton - an event handler registered by a screen would outlive it.
/// </para>
/// </summary>
public interface IConnectivityService
{
    bool IsConnected { get; }
}
