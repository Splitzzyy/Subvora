namespace SubVora.Mobile.Api.Dtos;

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PreferredCurrency { get; set; } = string.Empty;
    public int? DefaultAlertDaysAdvance { get; set; }
}

public class UpdateUserProfileRequest
{
    public string PreferredCurrency { get; set; } = string.Empty;
    public int? DefaultAlertDaysAdvance { get; set; }
}
