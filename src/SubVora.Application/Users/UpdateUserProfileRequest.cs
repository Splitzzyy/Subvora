namespace SubVora.Application.Users;

public class UpdateUserProfileRequest
{
    public string PreferredCurrency { get; set; } = string.Empty;
    public int? DefaultAlertDaysAdvance { get; set; }
}
