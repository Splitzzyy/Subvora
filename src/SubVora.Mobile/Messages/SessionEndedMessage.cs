namespace SubVora.Mobile.Messages;

/// <summary>
/// Published when the session ends, by sign-out or by an unrecoverable 401. The banner clears on
/// it: one user's spend must never still be on screen for the next person to sign in on the same
/// device, and the login screen is inside the Shell that hosts the banner.
/// </summary>
public sealed record SessionEndedMessage;
