using Microsoft.AspNetCore.Components.Forms;
using ForgeFlow.Web.Drafts;
using ForgeFlow.Web.Localization;

namespace ForgeFlow.Web.Components.Pages;

public sealed class SampleFormState
{
    private readonly ValidationMessageStore messages;

    public SampleFormState()
    {
        EditContext = new EditContext(this);
        messages = new ValidationMessageStore(EditContext);
    }

    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string AmountRaw { get; set; } = "0";
    public string AmountCulture { get; set; } = CulturePolicy.English;
    public string DateRaw { get; set; } = string.Empty;
    public string DateCulture { get; set; } = CulturePolicy.Persian;
    public string CurrencyCode { get; set; } = "USD";
    public string ModeValue { get; set; } = "summary";
    public bool ValidationAttempted { get; private set; }
    public EditContext EditContext { get; }
    public bool Submitted { get; private set; }
    public bool HasNameError => EditContext.GetValidationMessages(() => Name).Any();
    public bool HasAmountError => EditContext.GetValidationMessages(() => AmountRaw).Any();
    public bool HasDateError => EditContext.GetValidationMessages(() => DateRaw).Any();
    public bool HasCurrencyError => EditContext.GetValidationMessages(() => CurrencyCode).Any();
    public bool HasModeError => EditContext.GetValidationMessages(() => ModeValue).Any();
    public DateOnly ParsedDate { get; private set; }
    public decimal ParsedAmount { get; private set; }
    public string ParsedCurrencyCode { get; private set; } = string.Empty;
    public SampleMode ParsedMode { get; private set; }

    public sealed record CanonicalValues(string Name, DateOnly Date, decimal Amount, string CurrencyCode, SampleMode Mode);

    public void Validate(string requiredMessage)
    {
        ValidationAttempted = true;
        messages.Clear();
        Submitted = !string.IsNullOrWhiteSpace(Name);
        if (!Submitted)
            messages.Add(() => Name, requiredMessage);
        EditContext.NotifyValidationStateChanged();
    }

    public void Validate(string nameRequired, string amountInvalid, DisplayFormatter formatter)
    {
        Validate(nameRequired);
        if (!formatter.TryNumber(AmountRaw, AmountCulture, out var value) || decimal.Round(value, 2) != value)
        {
            messages.Add(() => AmountRaw, amountInvalid);
            Submitted = false;
        }
        EditContext.NotifyValidationStateChanged();
    }

    public void Validate(string nameRequired, string dateInvalid, string amountInvalid, string currencyInvalid,
        string modeInvalid, DisplayFormatter formatter)
    {
        ValidationAttempted = true;
        messages.Clear();
        var valid = !string.IsNullOrWhiteSpace(Name);
        if (!valid) messages.Add(() => Name, nameRequired);

        var dateValid = formatter.TryDateOnly(DateRaw, DateCulture, out var date);
        if (!dateValid) messages.Add(() => DateRaw, dateInvalid);
        else ParsedDate = date;
        valid &= dateValid;

        var amountValid = formatter.TryNumber(AmountRaw, AmountCulture, out var amount) && decimal.Round(amount, 2) == amount;
        if (!amountValid) messages.Add(() => AmountRaw, amountInvalid);
        else ParsedAmount = amount;
        valid &= amountValid;

        var currencyValid = formatter.TryCurrencyCode(CurrencyCode, out var currency);
        if (!currencyValid) messages.Add(() => CurrencyCode, currencyInvalid);
        else ParsedCurrencyCode = currency;
        valid &= currencyValid;

        var modeValid = DisplayFormatter.TrySampleMode(ModeValue, out var mode);
        if (!modeValid) messages.Add(() => ModeValue, modeInvalid);
        else ParsedMode = mode;
        valid &= modeValid;

        Submitted = valid;
        EditContext.NotifyValidationStateChanged();
    }

    public CanonicalValues CreateCanonicalValues()
    {
        if (!Submitted) throw new InvalidOperationException("The sample must validate before canonical values are captured.");
        return new CanonicalValues(Name, ParsedDate, ParsedAmount, ParsedCurrencyCode, ParsedMode);
    }

    public void Restore(DraftSnapshot snapshot)
    {
        Name = snapshot.Fields[nameof(Name)].Raw;
        Notes = snapshot.Fields[nameof(Notes)].Raw;
        AmountRaw = snapshot.Fields[nameof(AmountRaw)].Raw;
        AmountCulture = snapshot.Fields[nameof(AmountRaw)].InputCulture!;
        DateRaw = snapshot.Fields[nameof(DateRaw)].Raw;
        DateCulture = snapshot.Fields[nameof(DateRaw)].InputCulture!;
        CurrencyCode = snapshot.Fields[nameof(CurrencyCode)].Raw;
        ModeValue = snapshot.Fields[nameof(ModeValue)].Raw;
        ValidationAttempted = snapshot.ValidationAttempted;
        foreach (var field in snapshot.Dirty)
            EditContext.NotifyFieldChanged(new FieldIdentifier(this, field));
    }

    public void Reset(string culture = CulturePolicy.Persian)
    {
        Name = string.Empty; Notes = string.Empty; AmountRaw = "0";
        AmountCulture = CulturePolicy.Normalize(culture) ?? CulturePolicy.Persian;
        DateRaw = string.Empty; DateCulture = AmountCulture;
        CurrencyCode = "USD"; ModeValue = "summary";
        ParsedDate = default; ParsedAmount = default; ParsedCurrencyCode = string.Empty; ParsedMode = default;
        ValidationAttempted = false; Submitted = false;
        messages.Clear(); EditContext.MarkAsUnmodified(); EditContext.NotifyValidationStateChanged();
    }
}
