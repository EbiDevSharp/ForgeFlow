using ForgeFlow.Application.BusinessPartners;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Common;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class BusinessPartnerTests
{
    [Theory]
    [InlineData("09123456789", "09123456789")]
    [InlineData("۰۹۱۲۳۴۵۶۷۸۹", "09123456789")]
    [InlineData("  ۰۹۳۵۱۲۳۴۵۶۷  ", "09351234567")]
    public void OptionalMobile_accepts_valid_11_digit_starting_with_09_and_normalizes_persian_digits(string input, string expected)
    {
        var result = BusinessPartnerText.OptionalMobile(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0912345678")] // 10 digits
    [InlineData("091234567890")] // 12 digits
    [InlineData("08123456789")] // doesn't start with 09
    [InlineData("0912abc5678")] // contains letters
    [InlineData("0912345-678")] // contains dash
    public void OptionalMobile_rejects_invalid_mobiles(string input)
    {
        var ex = Assert.Throws<BusinessPartnerRuleException>(() => BusinessPartnerText.OptionalMobile(input));
        Assert.Equal(BusinessPartnerErrors.MobileInvalid, ex.Message);
    }

    [Theory]
    [InlineData("02188776655", "02188776655")]
    [InlineData("۰۲۱۸۸۷۷۶۶۵۵", "02188776655")]
    [InlineData("03132221100", "03132221100")]
    public void OptionalPhone_accepts_valid_phone_starting_with_0_and_normalizes_persian_digits(string input, string expected)
    {
        var result = BusinessPartnerText.OptionalPhone(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("2188776655")] // doesn't start with 0
    [InlineData("021-8877665")] // contains non-digits
    [InlineData("02188abc655")] // contains letters
    public void OptionalPhone_rejects_invalid_phones(string input)
    {
        var ex = Assert.Throws<BusinessPartnerRuleException>(() => BusinessPartnerText.OptionalPhone(input));
        Assert.Equal(BusinessPartnerErrors.PhoneInvalid, ex.Message);
    }

    [Theory]
    [InlineData("0012345678", "0012345678")]
    [InlineData("۰۰۱۲۳۴۵۶۷۸", "0012345678")]
    public void OptionalNationalCode_accepts_10_digits_and_normalizes(string input, string expected)
    {
        var result = BusinessPartnerText.OptionalNationalCode(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("123456789")] // 9 digits
    [InlineData("12345678901")] // 11 digits
    [InlineData("123456789a")] // contains letter
    [InlineData("12345-6789")] // contains dash
    public void OptionalNationalCode_rejects_invalid_length_or_chars(string input)
    {
        var ex = Assert.Throws<BusinessPartnerRuleException>(() => BusinessPartnerText.OptionalNationalCode(input));
        Assert.Equal(BusinessPartnerErrors.NationalCodeInvalid, ex.Message);
    }

    [Theory]
    [InlineData("10101234567", "10101234567")]
    [InlineData("۱۰۱۰۱۲۳۴۵۶۷", "10101234567")]
    public void OptionalDigits_for_legal_id_accepts_numeric_only(string input, string expected)
    {
        var result = BusinessPartnerText.OptionalDigits(input, 30, BusinessPartnerErrors.NationalLegalIdInvalid);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1010123456a")]
    [InlineData("1010-123456")]
    public void OptionalDigits_for_legal_id_rejects_non_numeric(string input)
    {
        var ex = Assert.Throws<BusinessPartnerRuleException>(() =>
            BusinessPartnerText.OptionalDigits(input, 30, BusinessPartnerErrors.NationalLegalIdInvalid));
        Assert.Equal(BusinessPartnerErrors.NationalLegalIdInvalid, ex.Message);
    }

    [Fact]
    public void BusinessPartner_Create_for_Natural_person_stores_separate_first_and_last_name()
    {
        var partner = BusinessPartner.Create(
            organizationId: 1,
            code: "BP-00001",
            personType: PersonType.Natural,
            name: null,
            firstName: "علی",
            lastName: "محمدی",
            nationalCode: "0012345678",
            companyName: null,
            nationalLegalId: null,
            economicCode: null,
            mobile: "09121112233",
            phone: "02188776655",
            email: "ali@example.com",
            website: null,
            country: "ایران",
            province: "تهران",
            city: "تهران",
            address: "خیابان ولیعصر",
            postalCode: null,
            notes: null,
            roleIds: [BusinessPartnerRoleDefinition.RoleSupplier]);

        Assert.Equal("علی محمدی", partner.Name);
        Assert.Equal("علی", partner.FirstName);
        Assert.Equal("محمدی", partner.LastName);
        Assert.Equal("0012345678", partner.NationalCode);
        Assert.Equal("09121112233", partner.Mobile);
        Assert.Equal("02188776655", partner.Phone);
        Assert.True(partner.IsSupplier);
        Assert.False(partner.IsCustomer);
    }

    [Fact]
    public void BusinessPartner_Create_for_Legal_person_stores_company_name_and_contact_names()
    {
        var partner = BusinessPartner.Create(
            organizationId: 1,
            code: "BP-00002",
            personType: PersonType.Legal,
            name: null,
            firstName: "رضا",
            lastName: "حسینی",
            nationalCode: null,
            companyName: "فولاد مبارکه",
            nationalLegalId: "10100012345",
            economicCode: "4111222333",
            mobile: "09131112233",
            phone: "03132221100",
            email: "info@steel.ir",
            website: "https://steel.ir",
            country: "ایران",
            province: "اصفهان",
            city: "مبارکه",
            address: "کیلومتر ۶۵ جاده اصفهان",
            postalCode: null,
            notes: null,
            roleIds: [BusinessPartnerRoleDefinition.RoleCustomer, BusinessPartnerRoleDefinition.RoleContractor]);

        Assert.Equal("فولاد مبارکه", partner.Name);
        Assert.Equal("فولاد مبارکه", partner.CompanyName);
        Assert.Equal("رضا", partner.FirstName);
        Assert.Equal("حسینی", partner.LastName);
        Assert.Equal("10100012345", partner.NationalLegalId);
        Assert.Equal("4111222333", partner.EconomicCode);
        Assert.True(partner.IsCustomer);
        Assert.True(partner.IsContractor);
        Assert.False(partner.IsSupplier);
    }

    [Fact]
    public void PartnerLookupQuery_defaults_and_contracts_are_valid()
    {
        var query = new PartnerLookupQuery();
        Assert.Null(query.Search);
        Assert.Equal(PartnerLookupRole.All, query.Role);
        Assert.Equal(1, query.Page);
        Assert.Equal(10, query.PageSize);
    }

    [Fact]
    public void PartnerLookupItem_preserves_attributes()
    {
        var item = new PartnerLookupItem(
            BusinessPartnerId: 10,
            TargetId: 5,
            Code: "BP-0012",
            Name: "فولاد البرز",
            PersonType: PersonType.Legal,
            CompanyName: "فولاد البرز",
            NationalCode: null,
            NationalLegalId: "10100011122",
            EconomicCode: "4111333444",
            Mobile: "09121112233",
            Phone: "02188776655",
            City: "کرج",
            RoleIds: [1]);

        Assert.Equal(10, item.BusinessPartnerId);
        Assert.Equal(5, item.TargetId);
        Assert.Equal("BP-0012", item.Code);
        Assert.Equal("فولاد البرز", item.Name);
        Assert.Equal("کرج", item.City);
        Assert.Equal("09121112233", item.Mobile);
        Assert.Single(item.RoleIds);
    }
}
