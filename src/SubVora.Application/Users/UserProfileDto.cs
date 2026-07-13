namespace SubVora.Application.Users;

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PreferredCurrency { get; set; } = string.Empty;
    public int? DefaultAlertDaysAdvance { get; set; }
}
