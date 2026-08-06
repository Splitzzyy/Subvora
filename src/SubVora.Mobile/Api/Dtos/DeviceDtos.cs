namespace SubVora.Mobile.Api.Dtos;

public class RegisterDeviceTokenRequest
{
    public string Token { get; set; } = string.Empty;

    /// <summary>"Android" or "iOS" - the backend validator rejects anything else.</summary>
    public string Platform { get; set; } = string.Empty;
}
