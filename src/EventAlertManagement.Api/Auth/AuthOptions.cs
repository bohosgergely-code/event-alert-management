namespace EventAlertManagement.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";
    public string Issuer { get; set; } = "EventAlertManagement";
    public string Audience { get; set; } = "EventAlertManagement.Web";

    /// <summary>Secret used to sign tokens. At least 32 characters. Never commit a real one.</summary>
    public string SigningKey { get; set; } = "";

    /// <summary>Key that grants the admin role. Empty means admin sign-in is switched off (fails closed).</summary>
    public string AdminKey { get; set; } = "";
    public int TokenHours { get; set; } = 8;

    /// <summary>Sign-in attempts allowed per client address and minute. Slows down guessing of the admin key.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;
}

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}
