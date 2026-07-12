namespace SubVora.Application.Devices;

public class RegisterDeviceTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
}
