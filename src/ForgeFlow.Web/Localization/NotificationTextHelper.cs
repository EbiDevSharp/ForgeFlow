using System.Text.Json;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Users;
using Microsoft.Extensions.Localization;

namespace ForgeFlow.Web.Localization;

public sealed record LocalizedNotificationContent(
    string Title,
    string Body,
    string? ActionLabel);

public static class NotificationTextHelper
{
    public static LocalizedNotificationContent Resolve(
        UserNotificationSnapshot item,
        IStringLocalizer<SharedResources> text)
    {
        var args = ParseJsonArgs(item.ParamsJson);
        string Get(string key, string fallback = "") => args.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : fallback;

        return item.TemplateCode switch
        {
            NotificationTemplateCatalog.NewDeviceSignIn => new(
                text["NotifTitle_NewDeviceSignIn"],
                text["NotifBody_NewDeviceSignIn", Get("device", "Browser"), Get("ip", "—")],
                text["NotifAction_ViewSessions"]),
            NotificationTemplateCatalog.FailedSignInAttempts => new(
                text["NotifTitle_FailedSignInAttempts", Get("count", "1")],
                text["NotifBody_FailedSignInAttempts", Get("ip", "—")],
                text["NotifAction_ViewActivity"]),
            NotificationTemplateCatalog.RoleOrAccessChanged => new(
                text["NotifTitle_RoleOrAccessChanged"],
                text["NotifBody_RoleOrAccessChanged", ResolveRoleLabel(Get("role", "—"), text), Get("scope", "—")],
                text["NotifAction_ViewActivity"]),
            NotificationTemplateCatalog.MyProductionOrderStatusChanged => new(
                text["NotifTitle_MyOrderChanged", Get("orderNumber", "—")],
                text["NotifBody_MyOrderChanged", ResolveOrderStatusLabel(Get("fromStatus", "—"), text), ResolveOrderStatusLabel(Get("toStatus", "—"), text)],
                text["NotifAction_ViewOrder"]),
            NotificationTemplateCatalog.PasswordChangedAlert => new(
                text["NotifTitle_PasswordChanged"],
                text["NotifBody_PasswordChanged", Get("device", "Browser")],
                text["NotifAction_ViewSessions"]),
            _ => new(item.TemplateCode, string.Empty, null)
        };
    }

    public static string FormatRelativeOrClock(
        DateTime utcTime,
        DisplayFormatter format,
        IStringLocalizer<SharedResources> text)
    {
        var elapsed = DateTime.UtcNow - utcTime;
        if (elapsed < TimeSpan.FromMinutes(2))
            return text["NotifTimeNow"];
        if (elapsed < TimeSpan.FromHours(1))
            return text["NotifTimeMinutes", Math.Max(1, (int)elapsed.TotalMinutes)];
        if (elapsed < TimeSpan.FromHours(24))
        {
            var nowLocal = format.ToDisplayDateTime(DateTime.UtcNow).Date;
            var itemLocal = format.ToDisplayDateTime(utcTime);
            if (itemLocal.Date == nowLocal)
                return text["NotifTimeHours", Math.Max(1, (int)elapsed.TotalHours)];
            return text["NotifTimeYesterday", itemLocal.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)];
        }
        if (elapsed < TimeSpan.FromDays(14))
            return text["NotifTimeDays", Math.Max(1, (int)elapsed.TotalDays)];
        return format.Timestamp(utcTime, " - ");
    }

    public static string SeverityIconClass(byte severity) => severity switch
    {
        (byte)NotificationSeverity.Warning => "notif-ico--warning",
        (byte)NotificationSeverity.Danger => "notif-ico--danger",
        (byte)NotificationSeverity.Success => "notif-ico--success",
        _ => "notif-ico--info"
    };

    public static string SeverityGlyphClass(byte severity) => severity switch
    {
        (byte)NotificationSeverity.Warning => "ff-icon--warning",
        (byte)NotificationSeverity.Danger => "ff-icon--alert-circle",
        (byte)NotificationSeverity.Success => "ff-icon--check",
        _ => "ff-icon--role"
    };

    public static string ResolveEventLabel(string eventCode, IStringLocalizer<SharedResources> text)
    {
        if (AuditEventCatalog.TryGet(eventCode, out var desc))
            return text[desc.ResourceKey];
        return eventCode;
    }

    public static string ResolveOrderStatusLabel(string rawStatus, IStringLocalizer<SharedResources> text) => rawStatus switch
    {
        "Draft" => text["PoStatusDraft"],
        "Released" => text["PoStatusReleased"],
        "InProgress" => text["PoStatusInProgress"],
        "Completed" => text["PoStatusCompleted"],
        "Cancelled" => text["PoStatusCancelled"],
        _ => rawStatus
    };

    public static string ResolveRoleLabel(string rawRole, IStringLocalizer<SharedResources> text) => rawRole switch
    {
        "OrganizationAdministrator" => text["RoleBuiltInOrgAdmin"],
        "SiteOperator" => text["RoleBuiltInSiteOperator"],
        "DeploymentProvisioner" => text["RoleBuiltInProvisioner"],
        "OrganizationMembership" => text["NotifRoleOrgMembership"],
        "SiteMembership" => text["NotifRoleSiteMembership"],
        "DirectPermissions" => text["NotifRoleDirectPermissions"],
        _ => rawRole
    };

    private static Dictionary<string, string> ParseJsonArgs(string? json)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return map;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                map[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.ToString();
            }
        }
        catch (JsonException) { }
        return map;
    }
}
