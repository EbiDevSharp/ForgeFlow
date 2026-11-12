using System.Globalization;

namespace ForgeFlow.Domain.Organizations;

public enum DocumentSequenceType : byte
{
    SalesOrder = 1,
    DeliveryOrder = 2,
    PurchaseOrder = 3,
    PurchaseReceipt = 4,
    ProductionOrder = 5,
    InventoryTransaction = 6,
    InventoryReversal = 7,
    QualityInspection = 8,
    QualityScrap = 9
}

public enum DocumentYearMode : byte
{
    None = 0,
    Gregorian = 1,
    Persian = 2
}

/// <summary>Organization-scoped configurable auto-numbering sequence for operational documents.</summary>
public sealed class DocumentSequence
{
    public const int MaxPrefixLength = 12;
    public const int MinPaddingDigits = 3;
    public const int MaxPaddingDigits = 8;

    private static readonly PersianCalendar PersianCal = new();

    private DocumentSequence()
    {
        Prefix = string.Empty;
        Separator = "-";
    }

    private DocumentSequence(
        long organizationId,
        DocumentSequenceType documentType,
        string prefix,
        string separator,
        DocumentYearMode yearMode,
        byte paddingDigits,
        long nextNumber,
        bool allowManualOverride)
    {
        if (organizationId <= 0)
            throw new DocumentSequenceRuleException(DocumentSequenceErrors.OrganizationInvalid);
        if (!Enum.IsDefined(documentType))
            throw new DocumentSequenceRuleException(DocumentSequenceErrors.DocumentTypeInvalid);

        OrganizationId = organizationId;
        DocumentType = documentType;
        Prefix = ValidatePrefix(prefix);
        Separator = ValidateSeparator(separator);
        YearMode = ValidateYearMode(yearMode);
        PaddingDigits = ValidatePadding(paddingDigits);
        NextNumber = ValidateNextNumber(nextNumber);
        AllowManualOverride = allowManualOverride;
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public DocumentSequenceType DocumentType { get; private set; }
    public string Prefix { get; private set; }
    public string Separator { get; private set; }
    public DocumentYearMode YearMode { get; private set; }
    public byte PaddingDigits { get; private set; }
    public long NextNumber { get; private set; }
    public bool AllowManualOverride { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static DocumentSequence CreateDefault(long organizationId, DocumentSequenceType documentType)
    {
        var prefix = DefaultPrefixFor(documentType);
        return new DocumentSequence(organizationId, documentType, prefix, "-", DocumentYearMode.None, 5, 1, false);
    }

    public static DocumentSequence Create(
        long organizationId,
        DocumentSequenceType documentType,
        string prefix,
        string separator,
        DocumentYearMode yearMode,
        byte paddingDigits,
        long nextNumber,
        bool allowManualOverride)
        => new(organizationId, documentType, prefix, separator, yearMode, paddingDigits, nextNumber, allowManualOverride);

    public bool Update(
        string prefix,
        string separator,
        DocumentYearMode yearMode,
        byte paddingDigits,
        long nextNumber,
        bool allowManualOverride)
    {
        var nextPrefix = ValidatePrefix(prefix);
        var nextSep = ValidateSeparator(separator);
        var nextYear = ValidateYearMode(yearMode);
        var nextPad = ValidatePadding(paddingDigits);
        var nextNum = ValidateNextNumber(nextNumber);

        if (string.Equals(Prefix, nextPrefix, StringComparison.Ordinal)
            && string.Equals(Separator, nextSep, StringComparison.Ordinal)
            && YearMode == nextYear
            && PaddingDigits == nextPad
            && NextNumber == nextNum
            && AllowManualOverride == allowManualOverride)
        {
            return false;
        }

        Prefix = nextPrefix;
        Separator = nextSep;
        YearMode = nextYear;
        PaddingDigits = nextPad;
        NextNumber = nextNum;
        AllowManualOverride = allowManualOverride;
        return true;
    }

    public string FormatPreview(DateTime utcNow) => FormatNumber(NextNumber, utcNow);

    public string AllocateNextNumber(DateTime utcNow)
    {
        var formatted = FormatNumber(NextNumber, utcNow);
        NextNumber++;
        return formatted;
    }

    public string FormatNumber(long sequenceNumber, DateTime utcNow)
    {
        var seqStr = sequenceNumber.ToString($"D{PaddingDigits}", CultureInfo.InvariantCulture);
        var yearPart = YearMode switch
        {
            DocumentYearMode.Gregorian => utcNow.Year.ToString("D4", CultureInfo.InvariantCulture),
            DocumentYearMode.Persian => PersianCal.GetYear(utcNow).ToString("D4", CultureInfo.InvariantCulture),
            _ => null
        };

        var result = yearPart is null
            ? $"{Prefix}{Separator}{seqStr}"
            : $"{Prefix}{Separator}{yearPart}{Separator}{seqStr}";

        return result.Length <= 32 ? result : result[..32];
    }

    public static string DefaultPrefixFor(DocumentSequenceType documentType) => documentType switch
    {
        DocumentSequenceType.SalesOrder => "SO",
        DocumentSequenceType.DeliveryOrder => "DO",
        DocumentSequenceType.PurchaseOrder => "PO",
        DocumentSequenceType.PurchaseReceipt => "RCV",
        DocumentSequenceType.ProductionOrder => "MO",
        DocumentSequenceType.InventoryTransaction => "INV",
        DocumentSequenceType.InventoryReversal => "REV",
        DocumentSequenceType.QualityInspection => "QC",
        DocumentSequenceType.QualityScrap => "QS",
        _ => "DOC"
    };

    private static string ValidatePrefix(string? raw)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxPrefixLength)
            throw new DocumentSequenceRuleException(DocumentSequenceErrors.PrefixInvalid, ("maxLength", MaxPrefixLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new DocumentSequenceRuleException(DocumentSequenceErrors.PrefixInvalid, ("maxLength", MaxPrefixLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new DocumentSequenceRuleException(DocumentSequenceErrors.PrefixInvalid, ("maxLength", MaxPrefixLength));
        }
        return trimmed;
    }

    private static string ValidateSeparator(string? raw)
    {
        var s = raw?.Trim() ?? string.Empty;
        if (s is not ("" or "-" or "_"))
            throw new DocumentSequenceRuleException(DocumentSequenceErrors.SeparatorInvalid);
        return s;
    }

    private static DocumentYearMode ValidateYearMode(DocumentYearMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new DocumentSequenceRuleException(DocumentSequenceErrors.YearModeInvalid);
        return mode;
    }

    private static byte ValidatePadding(byte padding)
    {
        if (padding < MinPaddingDigits || padding > MaxPaddingDigits)
            throw new DocumentSequenceRuleException(DocumentSequenceErrors.PaddingInvalid);
        return padding;
    }

    private static long ValidateNextNumber(long value)
    {
        if (value < 1)
            throw new DocumentSequenceRuleException(DocumentSequenceErrors.NextNumberInvalid);
        return value;
    }
}
