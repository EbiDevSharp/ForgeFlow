using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ForgeFlow.Web.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ResourceContractTests
{
    // An explicit registry makes dynamic keys and parameter counts reviewable.
    private static readonly Dictionary<string, Dictionary<string, int>> Contracts = new()
    {
        [nameof(SampleResources)] = new()
        {
            ["Title"] = 0, ["Introduction"] = 0, ["NameLabel"] = 0, ["NameRequired"] = 0,
            ["ValidateButton"] = 0, ["ValidationSucceeded"] = 1, ["DateLabel"] = 0, ["NumberLabel"] = 0,
            ["FormLegend"] = 0, ["LanguageLabel"] = 0, ["LanguagePersian"] = 0, ["LanguageEnglish"] = 0,
            ["AmountLabel"] = 0, ["AmountInvalid"] = 0, ["AmountPreview"] = 0, ["NotesLabel"] = 0,
            ["DiscardButton"] = 0, ["DraftDiscarded"] = 0, ["DraftRejected"] = 0,
            ["DraftUnavailable"] = 0, ["SwitchFailed"] = 0,
            ["DialogOpen"] = 0, ["DialogTitle"] = 0, ["DialogContent"] = 0, ["DialogClose"] = 0,
            ["TableTitle"] = 0, ["TableCaption"] = 0, ["TableFieldHeading"] = 0, ["TableValueHeading"] = 0,
            ["SyntheticName"] = 0, ["SyntheticAmount"] = 0, ["TooltipButton"] = 0, ["TooltipContent"] = 0,
            ["SampleRefresh"] = 0, ["SampleLoading"] = 0, ["SampleEmpty"] = 0, ["FeedbackTitle"] = 0,
            ["NotificationTrigger"] = 0, ["NotificationTitle"] = 0, ["NotificationContent"] = 0,
            ["MessageDismiss"] = 0, ["IdentityFixtureTitle"] = 0, ["IdentityFixtureScope"] = 0,
            ["IdentityFixtureTrigger"] = 0, ["IdentityErrorDuplicateUserName"] = 1,
            ["DateInputLabel"] = 0, ["DateInputHint"] = 0, ["DateInvalid"] = 0, ["DatePreview"] = 0,
            ["CurrencyLabel"] = 0, ["CurrencyUsd"] = 0, ["CurrencyEur"] = 0, ["CurrencyIrr"] = 0,
            ["CurrencyInvalid"] = 0, ["CurrencyPreview"] = 0, ["ChooseCurrency"] = 0,
            ["SampleModeLabel"] = 0, ["SampleModeSummary"] = 0, ["SampleModeDetailed"] = 0,
            ["SampleModeInvalid"] = 0, ["ChooseMode"] = 0, ["CanonicalValuesTitle"] = 0,
            ["SubmittedDate"] = 0, ["SubmittedAmount"] = 0, ["SubmittedCurrency"] = 0, ["SubmittedMode"] = 0
        },
        [nameof(SharedResources)] = new()
        {
            ["Navigation"] = 0, ["SampleNavigation"] = 0, ["AppTitle"] = 0,
            ["OpenNavigation"] = 0, ["CloseNavigation"] = 0, ["CollapseNavigation"] = 0, ["ErrorTitle"] = 0,
            ["UnexpectedError"] = 0, ["Reload"] = 0, ["NotFoundTitle"] = 0, ["NotFoundMessage"] = 0
        }
    };

    [Fact]
    public void Explicit_catalogs_have_complete_keys_and_matching_placeholder_contracts()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Catalogs", "Localization");
        var files = Directory.GetFiles(directory, "*.resx");
        Assert.Equal(Contracts.Count * CulturePolicy.Supported.Count, files.Length);
        foreach (var (resource, contract) in Contracts)
        {
            Dictionary<string, string>? baseline = null;
            foreach (var culture in CulturePolicy.Supported)
            {
                var path = Path.Combine(directory, $"{resource}.{culture}.resx");
                Assert.True(File.Exists(path), path);
                var catalog = ReadCatalog(XDocument.Load(path));
                RequireKeys(catalog, contract.Keys);
                foreach (var (key, parameterCount) in contract)
                {
                    var signature = Signature(catalog[key]);
                    Assert.Equal(Enumerable.Range(0, parameterCount), signature.Select(x => x.Index).Distinct().Order());
                    if (baseline is not null)
                        Assert.Equal(Signature(baseline[key]), signature);
                }
                baseline = catalog;
            }
        }
    }

    [Fact]
    public void Every_Razor_key_is_registered_and_every_registered_key_is_used()
    {
        var used = Contracts.ToDictionary(x => x.Key, _ => new HashSet<string>());
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Sources"), "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            var resource = Regex.Match(source, @"IStringLocalizer<(?<resource>\w+)> Text");
            foreach (Match reference in Regex.Matches(source, "Text\\[\"(?<key>[^\"]+)\""))
            {
                Assert.True(resource.Success, path);
                var resourceName = resource.Groups["resource"].Value;
                var key = reference.Groups["key"].Value;
                Assert.Contains(key, Contracts[resourceName].Keys);
                used[resourceName].Add(key);
            }
        }
        used[nameof(SampleResources)].UnionWith(
            ["CurrencyUsd", "CurrencyEur", "CurrencyIrr", "SampleModeSummary", "SampleModeDetailed"]);
        foreach (var (resource, keys) in used)
            Assert.Equal(Contracts[resource].Keys.Order(), keys.Order());
    }

    [Fact]
    public void Currency_and_sample_mode_resource_maps_are_exhaustive_for_both_cultures()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Catalogs", "Localization");
        var currencyKeys = DisplayFormatter.SupportedCurrencyCodes.Select(DisplayFormatter.CurrencyResourceKey).ToArray();
        var modeValues = Enum.GetValues<SampleMode>();
        var modeKeys = modeValues.Select(DisplayFormatter.SampleModeResourceKey).ToArray();
        Assert.Equal(DisplayFormatter.SupportedCurrencyCodes.Count, currencyKeys.Distinct().Count());
        Assert.Equal(modeValues.Length, modeKeys.Distinct().Count());
        foreach (var culture in CulturePolicy.Supported)
        {
            var catalog = ReadCatalog(XDocument.Load(Path.Combine(directory, $"SampleResources.{culture}.resx")));
            Assert.All(currencyKeys.Concat(modeKeys), key => Assert.Contains(key, catalog.Keys));
        }
    }

    [Theory]
    [InlineData("<root><data name='A'><value>One</value></data><data name='A'><value>Two</value></data></root>")]
    [InlineData("<root><data name='A'><value> </value></data></root>")]
    [InlineData("<root><data><value>One</value></data></root>")]
    public void Invalid_catalog_fixtures_are_rejected(string xml)
        => Assert.Throws<InvalidDataException>(() => ReadCatalog(XDocument.Parse(xml)));

    [Theory]
    [InlineData("<root><data name='A'><value>One</value></data></root>")]
    [InlineData("<root><data name='A'><value>One</value></data><data name='B'><value>Two</value></data><data name='C'><value>Extra</value></data></root>")]
    public void Missing_or_extra_key_fixtures_are_rejected(string xml)
        => Assert.Throws<InvalidDataException>(() => RequireKeys(ReadCatalog(XDocument.Parse(xml)), ["A", "B"]));

    [Theory]
    [InlineData("{0", "{0}")]
    [InlineData("{0}", "{1}")]
    [InlineData("{0:N2}", "{0:N3}")]
    [InlineData("{0}", "No parameter")]
    public void Malformed_or_incompatible_placeholder_fixtures_are_rejected(string first, string second)
    {
        var rejected = false;
        try { rejected = !Signature(first).SequenceEqual(Signature(second)); }
        catch (FormatException) { rejected = true; }
        Assert.True(rejected);
    }

    [Fact]
    public void Escaped_braces_reordering_and_repeated_parameters_are_supported()
    {
        Assert.Equal(Signature("{{label}} {0:N2} {1} {0:N2}"), Signature("{1} {{{0:N2}}}"));
        Assert.Empty(Signature("{{0}}"));
    }

    private static Dictionary<string, string> ReadCatalog(XDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in document.Root!.Elements("data"))
        {
            var name = (string?)data.Attribute("name");
            var value = (string?)data.Element("value");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value) || !result.TryAdd(name, value))
                throw new InvalidDataException("Invalid resource entry");
        }
        return result;
    }

    private static void RequireKeys(Dictionary<string, string> catalog, IEnumerable<string> keys)
    {
        if (!catalog.Keys.Order().SequenceEqual(keys.Order()))
            throw new InvalidDataException("Resource key mismatch");
    }

    private static (int Index, string Format)[] Signature(string message)
    {
        _ = CompositeFormat.Parse(message);
        var result = new HashSet<(int Index, string Format)>();
        for (var position = 0; position < message.Length; position++)
        {
            if (message[position] != '{') continue;
            if (position + 1 < message.Length && message[position + 1] == '{')
            {
                position++;
                continue;
            }
            var end = message.IndexOf('}', position);
            var token = message[(position + 1)..end];
            var match = Regex.Match(token, @"^(?<index>\d+)(?:\s*,\s*-?\d+)?(?:\:(?<format>.*))?$");
            if (!match.Success) throw new FormatException();
            result.Add((int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture), match.Groups["format"].Value));
            position = end;
        }
        return result.OrderBy(x => x.Index).ThenBy(x => x.Format, StringComparer.Ordinal).ToArray();
    }
}
