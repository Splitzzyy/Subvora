namespace SubVora.Application.Devices;

public class DeviceTokenDto
{
    public Guid Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTimeOffset LastSeenAt { get; set; }
}
