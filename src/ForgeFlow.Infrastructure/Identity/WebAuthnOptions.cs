namespace ForgeFlow.Infrastructure.Identity;

public sealed class WebAuthnOptions
{
    public const string SectionName = "WebAuthn";
    public string? RpId { get; set; }
    public string? Origin { get; set; }
    public string RpName { get; set; } = "ForgeFlow";
}
