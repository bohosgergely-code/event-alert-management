using EventAlertManagement.Core.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace EventAlertManagement.Infrastructure.Security;

/// <summary>Encrypts channel settings with ASP.NET Core Data Protection (see ADR-014).</summary>
public sealed class DataProtectionSettingsProtector(IDataProtectionProvider provider) : ISettingsProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("EventAlertManagement.ChannelSettings.v1");

    public string Protect(string plain) => _protector.Protect(plain);
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
