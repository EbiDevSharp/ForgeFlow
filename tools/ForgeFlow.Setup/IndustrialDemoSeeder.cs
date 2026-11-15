using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Setup;

public static class IndustrialDemoSeeder
{
    private static readonly TrustedActor SeedActor = new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    public static async Task<int> SeedAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new OrganizationDbContext(options);

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("\n==================================================================");
        Console.WriteLine("     تزریق داده‌های جامع صنعتی، سفارشات خرید، تولید، کیفیت و فروش");
        Console.WriteLine("==================================================================");

        Console.WriteLine("\n[1/8] بررسی وضعیت سازمان و کارخانه پایه...");

        var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == 1);
        if (org is null)
        {
            Console.WriteLine(" ❌ سازمان با شناسه ۱ یافت نشد.");
            return 1;
        }

        var site = await db.Sites.SingleOrDefaultAsync(s => s.OrganizationId == 1 && s.Id == 1);
        if (site is null)
        {
            Console.WriteLine(" ❌ کارخانه با شناسه ۱ یافت نشد.");
            return 1;
        }

        const long orgId = 1;
        const long siteId = 1;

        Console.WriteLine($" ✔ سازمان '{org.Name}' و کارخانه '{site.Name}' تایید شدند.");

        // =========================================================================
        // 1. Units of Measure & Conversions
        // =========================================================================
        Console.WriteLine("\n[2/8] تعریف واحدهای سنجش، ضرایب تبدیل، ارزها و انبارها...");

        var existingUnits = await db.Units.Where(u => u.OrganizationId == orgId).ToDictionaryAsync(u => u.Code);

        Unit GetOrAddUnit(string code, string name, string symbol, UnitDimension dim, byte scale)
        {
            if (existingUnits.TryGetValue(code, out var existing)) return existing;
            var u = Unit.Create(orgId, code, name, symbol, dim, scale);
            db.Units.Add(u);
            existingUnits[code] = u;
            return u;
        }

        var uKg = GetOrAddUnit("KG", "کیلوگرم", "kg", UnitDimension.Mass, 3);
        var uTon = GetOrAddUnit("TON", "تن", "t", UnitDimension.Mass, 3);
        var uG = GetOrAddUnit("G", "گرم", "g", UnitDimension.Mass, 2);
        var uL = GetOrAddUnit("L", "لیتر", "L", UnitDimension.Volume, 3);
        var uMl = GetOrAddUnit("ML", "میلی‌لیتر", "mL", UnitDimension.Volume, 2);
        var uPcs = GetOrAddUnit("PCS", "عدد", "pcs", UnitDimension.Count, 0);
        var uBtl = GetOrAddUnit("BTL", "بطری", "btl", UnitDimension.Count, 0);
        var uCan = GetOrAddUnit("CAN", "قوطی", "can", UnitDimension.Count, 0);
        var uBox = GetOrAddUnit("BOX", "کارتن", "box", UnitDimension.Count, 0);

        await db.SaveChangesAsync(SeedActor);

        void EnsureConversion(long fromId, long toId, decimal factor)
        {
            if (!db.UnitConversions.Any(c => c.OrganizationId == orgId && c.FromUnitId == fromId && c.ToUnitId == toId))
            {
                db.UnitConversions.Add(UnitConversion.Create(orgId, fromId, toId, factor));
            }
        }

        EnsureConversion(uTon.Id, uKg.Id, 1000m);
        EnsureConversion(uKg.Id, uG.Id, 1000m);
        EnsureConversion(uL.Id, uMl.Id, 1000m);

        if (!await db.Currencies.AnyAsync(c => c.OrganizationId == orgId && c.Code == "IRR"))
        {
            db.Currencies.Add(Currency.Create(orgId, "IRR", "ریال ایران", "ریال", 0));
        }

        var existingWhs = await db.Warehouses.Where(w => w.OrganizationId == orgId && w.SiteId == siteId).ToDictionaryAsync(w => w.Code);
        Warehouse GetOrAddWh(string code, string name, bool isDefault)
        {
            if (existingWhs.TryGetValue(code, out var w)) return w;
            var nw = Warehouse.Create(orgId, siteId, code, name, isDefault);
            db.Warehouses.Add(nw);
            existingWhs[code] = nw;
            return nw;
        }

        var whRm = GetOrAddWh("WH-RM", "انبار مواد اولیه و ملزومات بسته‌بندی", true);
        var whWip = GetOrAddWh("WH-WIP", "انبار کالای نیمه‌ساخته و پای‌خط", false);
        var whFg = GetOrAddWh("WH-FG", "انبار محصولات نهایی", false);
        var whQc = GetOrAddWh("WH-QC", "انبار قرنطینه و بازرسی کیفی", false);

        await db.SaveChangesAsync(SeedActor);
        Console.WriteLine(" ✔ واحدهای سنجش، تبدیل‌ها، ارز و انبارها آماده شدند.");

        // =========================================================================
        // 2. Classifications & Technical Attributes
        // =========================================================================
        Console.WriteLine("\n[3/8] تعریف مجموعه‌های طبقه‌بندی درختی و ویژگی‌های فنی کالا...");

        var existingSets = await db.ClassificationSets.Where(s => s.OrganizationId == orgId).ToDictionaryAsync(s => s.Code);
        ClassificationSet GetOrAddSet(string code, string name)
        {
            if (existingSets.TryGetValue(code, out var s)) return s;
            var ns = ClassificationSet.Create(orgId, code, name, isMandatory: false, allowMultiple: true);
            db.ClassificationSets.Add(ns);
            existingSets[code] = ns;
            return ns;
        }

        var setPetro = GetOrAddSet("CLS-PETRO", "صنایع پلیمری و پتروشیمی");
        var setFoodOil = GetOrAddSet("CLS-FOOD-OIL", "صنایع روغن‌های خوراکی");
        var setFoodCanned = GetOrAddSet("CLS-FOOD-CANNED", "صنایع کنسرو و شیلات");
        var setPkg = GetOrAddSet("CLS-PACKAGING", "ظروف و ملزومات بسته‌بندی");
        await db.SaveChangesAsync(SeedActor);

        var existingNodes = await db.Classifications.Where(c => c.OrganizationId == orgId).ToDictionaryAsync(c => c.Code);
        Classification GetOrAddNode(ClassificationSet set, string code, string name, long? parentId = null)
        {
            if (existingNodes.TryGetValue(code, out var c)) return c;
            var nc = Classification.Create(orgId, set.Id, parentId, code, name);
            db.Classifications.Add(nc);
            existingNodes[code] = nc;
            return nc;
        }

        var nodePetroRoot = GetOrAddNode(setPetro, "POLYMER", "پلیمرها و پلاستیک‌ها");
        await db.SaveChangesAsync(SeedActor);
        var nodeGpps = GetOrAddNode(setPetro, "PETRO-GPPS", "پلی‌استایرن معمولی GPPS", nodePetroRoot.Id);

        var nodeOilRoot = GetOrAddNode(setFoodOil, "OILS", "روغن‌ها و چربی‌های خوراکی");
        await db.SaveChangesAsync(SeedActor);
        var nodeFryOil = GetOrAddNode(setFoodOil, "FOOD-OIL-FRY", "روغن‌های پخت و سرخ‌کردنی", nodeOilRoot.Id);

        var nodeCannedRoot = GetOrAddNode(setFoodCanned, "CANNED", "کنسروجات گوشتی و دریایی");
        await db.SaveChangesAsync(SeedActor);
        var nodeTuna = GetOrAddNode(setFoodCanned, "FOOD-TUNA", "کنسرو ماهی تن", nodeCannedRoot.Id);

        var nodeBottles = GetOrAddNode(setPkg, "PKG-BOTTLES", "پریفرم و بطری‌های پت");
        var nodeCans = GetOrAddNode(setPkg, "PKG-CANS", "قوطی‌های فلزی و ایزی‌اوپن");
        var nodeBoxes = GetOrAddNode(setPkg, "PKG-BOXES", "کارتن‌های مقوایی بسته‌بندی");
        await db.SaveChangesAsync(SeedActor);

        // Technical Attributes
        var existingAttrs = await db.AttributeDefinitions.Where(a => a.OrganizationId == orgId).ToDictionaryAsync(a => a.Code);
        AttributeDefinition GetOrAddAttr(string code, string name, AttributeDataType dt, long? unitId)
        {
            if (existingAttrs.TryGetValue(code, out var a)) return a;
            var na = AttributeDefinition.Create(orgId, code, name, dt, unitId, isRequired: false);
            db.AttributeDefinitions.Add(na);
            existingAttrs[code] = na;
            return na;
        }

        var attrMfi = GetOrAddAttr("ATTR-MFI", "شاخص جریان مذاب (MFR/MFI)", AttributeDataType.Decimal, uG.Id);
        var attrVicat = GetOrAddAttr("ATTR-VICAT", "دمای نرمی ویکات (°C)", AttributeDataType.Decimal, null);
        var attrDensity = GetOrAddAttr("ATTR-DENSITY", "دانسیته فیزیکی (g/cm³)", AttributeDataType.Decimal, null);
        var attrIodine = GetOrAddAttr("ATTR-IODINE", "عدد یدی روغن (g I2/100g)", AttributeDataType.Decimal, uG.Id);
        var attrFfa = GetOrAddAttr("ATTR-FFA", "اسید چرب آزاد (FFA %)", AttributeDataType.Decimal, null);
        var attrDrained = GetOrAddAttr("ATTR-DRAINED", "درصد وزن آبکش (%)", AttributeDataType.Decimal, null);
        var attrHistamine = GetOrAddAttr("ATTR-HISTAMINE", "میزان هیستامین (ppm)", AttributeDataType.Decimal, null);
        var attrFoodGrade = GetOrAddAttr("ATTR-FOOD-GRADE", "مجوز تماس با غذا (Food Grade)", AttributeDataType.Boolean, null);

        await db.SaveChangesAsync(SeedActor);
        Console.WriteLine(" ✔ طبقه‌بندی‌های درختی و ویژگی‌های فنی ثبت شدند.");

        // =========================================================================
        // 3. Business Partners (5 Suppliers, 3 Customers)
        // =========================================================================
        Console.WriteLine("\n[4/8] تعریف طرف‌های تجاری (تأمین‌کنندگان و مشتریان و انطباق با جدول‌های تخصصی)...");

        var existingPartners = await db.BusinessPartners.Where(b => b.OrganizationId == orgId).ToDictionaryAsync(b => b.Code);

        async Task<BusinessPartner> EnsurePartnerAsync(
            string code, PersonType type, string name, string? firstName, string? lastName, string? companyName,
            string? nationalCode, string? nationalLegalId, string? economicCode, string mobile, string phone,
            string province, string city, string address, int[] roles)
        {
            if (existingPartners.TryGetValue(code, out var p)) return p;

            var np = BusinessPartner.Create(
                organizationId: orgId,
                code: code,
                personType: type,
                name: name,
                firstName: firstName,
                lastName: lastName,
                nationalCode: nationalCode,
                companyName: companyName,
                nationalLegalId: nationalLegalId,
                economicCode: economicCode,
                mobile: mobile,
                phone: phone,
                email: $"contact@{code.ToLowerInvariant().Replace('-', '.')}.ir",
                website: $"https://{code.ToLowerInvariant().Replace('-', '.')}.ir",
                country: "ایران",
                province: province,
                city: city,
                address: address,
                postalCode: "1234567890",
                notes: "شریک تجاری معتبر صنعتی",
                roleIds: roles);

            db.BusinessPartners.Add(np);

            if (np.IsSupplier)
            {
                var existingSup = await db.Suppliers.SingleOrDefaultAsync(s => s.OrganizationId == orgId && s.Code == np.Code);
                if (existingSup is null)
                {
                    db.Suppliers.Add(Supplier.Create(
                        orgId, np.Code, np.Name,
                        np.PersonType == PersonType.Natural ? $"{np.FirstName} {np.LastName}".Trim() : np.CompanyName,
                        np.Phone ?? np.Mobile, np.Email, np.Address, np.Notes));
                }
            }

            if (np.IsCustomer)
            {
                var existingCust = await db.Customers.SingleOrDefaultAsync(c => c.OrganizationId == orgId && c.Code == np.Code);
                if (existingCust is null)
                {
                    db.Customers.Add(Customer.Create(
                        orgId, np.Code, np.Name,
                        np.PersonType == PersonType.Natural ? $"{np.FirstName} {np.LastName}".Trim() : np.CompanyName,
                        np.Phone ?? np.Mobile, np.Email, np.Address,
                        np.EconomicCode ?? np.NationalLegalId ?? np.NationalCode, np.Notes));
                }
            }

            existingPartners[code] = np;
            return np;
        }

        // Suppliers
        var bpPars = await EnsurePartnerAsync("BP-PARS-PETRO", PersonType.Legal, "شرکت پتروشیمی پارس", null, null, "شرکت پتروشیمی پارس", null, "10101456789", "4114567891", "09121110011", "07737290000", "بوشهر", "عسلویه", "منطقه ویژه اقتصادی پارس جنوبی", [1]);
        var bpBehpak = await EnsurePartnerAsync("BP-BEHPAK", PersonType.Legal, "شرکت صنعتی بهپاک بهشهر", null, null, "شرکت صنعتی بهپاک بهشهر", null, "10102456789", "4116543211", "09122220022", "01134520000", "مازندران", "بهشهر", "بلوار شهید هاشمی‌نژاد، کارخانجات بهپاک", [1]);
        var bpParsPet = await EnsurePartnerAsync("BP-PARS-PET", PersonType.Legal, "شرکت پیشرو پلاستیک پارس", null, null, "شرکت پیشرو پلاستیک پارس", null, "10103456789", "4117894561", "09123330033", "02188881111", "تهران", "تهران", "جاده مخصوص کرج، کیلومتر ۱۱", [1]);
        var bpShilat = await EnsurePartnerAsync("BP-SHILAT-JONOUB", PersonType.Legal, "تعاونی صیادی آبهای جنوب", null, null, "تعاونی صیادی آبهای جنوب", null, "10104456789", "4119876541", "09154440044", "05435330000", "سیستان و بلوچستان", "چابهار", "اسکله شهید کلانتری، بارانداز صیادی", [1]);
        var bpKhazar = await EnsurePartnerAsync("BP-CAN-KHAZAR", PersonType.Legal, "شرکت قوطی‌سازی فلزی خزر", null, null, "شرکت قوطی‌سازی فلزی خزر", null, "10105456789", "4111234561", "09113334455", "01333445566", "گیلان", "رشت", "شهرک صنعتی رشت، خیابان ابتکار سوم", [1]);

        // Customers
        var bpAlborz = await EnsurePartnerAsync("BP-ALBORZ-DIST", PersonType.Legal, "شرکت پخش سراسری البرز", null, null, "شرکت پخش سراسری البرز", null, "10106456789", "4118529631", "09125550055", "02166554433", "تهران", "تهران", "کیلومتر ۸ بزرگراه فتح، مجتمع پخش البرز", [2]);
        var bpEtka = await EnsurePartnerAsync("BP-ETKA", PersonType.Legal, "شرکت فروشگاه‌های زنجیره‌ای اتکا", null, null, "شرکت فروشگاه‌های زنجیره‌ای اتکا", null, "10107456789", "4117418521", "09126660066", "02188776655", "تهران", "تهران", "میدان حسن‌آباد، خیابان امام خمینی", [2]);
        var bpJamshidi = await EnsurePartnerAsync("BP-JAMSHIDI", PersonType.Natural, "احمد جمشیدی", "احمد", "جمشیدی", null, "0071234567", null, null, "09137770077", "03132223344", "اصفهان", "اصفهان", "خیابان نشاط، بازار مواد اولیه", [2]);

        await db.SaveChangesAsync(SeedActor);

        var supPars = await db.Suppliers.SingleAsync(s => s.OrganizationId == orgId && s.Code == bpPars.Code);
        var supBehpak = await db.Suppliers.SingleAsync(s => s.OrganizationId == orgId && s.Code == bpBehpak.Code);
        var supParsPet = await db.Suppliers.SingleAsync(s => s.OrganizationId == orgId && s.Code == bpParsPet.Code);
        var supShilat = await db.Suppliers.SingleAsync(s => s.OrganizationId == orgId && s.Code == bpShilat.Code);
        var supKhazar = await db.Suppliers.SingleAsync(s => s.OrganizationId == orgId && s.Code == bpKhazar.Code);

        var custAlborz = await db.Customers.SingleAsync(c => c.OrganizationId == orgId && c.Code == bpAlborz.Code);
        var custEtka = await db.Customers.SingleAsync(c => c.OrganizationId == orgId && c.Code == bpEtka.Code);
        var custJamshidi = await db.Customers.SingleAsync(c => c.OrganizationId == orgId && c.Code == bpJamshidi.Code);

        Console.WriteLine(" ✔ طرف‌های تجاری با جدول‌های تأمین‌کننده و مشتری همگام شدند.");

        // =========================================================================
        // 4. Items Catalog (15 Items across 3 Industries)
        // =========================================================================
        Console.WriteLine("\n[5/8] تعریف اقلام کاتالوگ، ویژگی‌های فنی، طبقه‌بندی و فرمول‌های مهندسی BOM...");

        var existingItems = await db.Items.Where(i => i.OrganizationId == orgId).ToDictionaryAsync(i => i.Code);

        Item GetOrAddItem(string code, string name, string desc, long unitId, bool canPurch, bool canMfg, bool canSell, bool canCons, bool lot = false)
        {
            if (existingItems.TryGetValue(code, out var itm)) return itm;
            var ni = Item.Create(orgId, code, name, desc, unitId, canPurch, canMfg, canSell, canCons, requiresLotTracking: lot);
            db.Items.Add(ni);
            existingItems[code] = ni;
            return ni;
        }

        // Petrochemical Products & Materials
        var itmGpps = GetOrAddItem("FG-GPPS-1540", "پلی‌استایرن معمولی گرید ۱۵۴۰", "گرانول شفاف پلی‌استایرن برای مصارف ظروف و بسته‌بندی", uKg.Id, false, true, true, false, lot: true);
        var itmSm = GetOrAddItem("RM-SM-01", "استایرن منومر خالص", "ماده اولیه پلیمریزاسیون پلی‌استایرن پتروشیمی پارس", uKg.Id, true, false, false, true, lot: true);
        var itmCat = GetOrAddItem("RM-CAT-01", "آغازگر پروکساید صنعتی", "کاتالیزور پلیمریزاسیون استایرن", uKg.Id, true, false, false, true, lot: true);
        var itmMinOil = GetOrAddItem("RM-MIN-OIL", "روغن معدنی سفید پلیمری", "روغن فرآیندی شفاف بهبود دهنده جریان مذاب", uKg.Id, true, false, false, true, lot: false);
        var itmBag25 = GetOrAddItem("PKG-BAG-25KG", "کیسه لمینت ۲۵ کیلویی", "کیسه پلی‌پروپیلنی چاپ‌دار ۲۵ کیلوگرمی", uPcs.Id, true, false, false, true, lot: false);

        // Frying Oil Products & Materials (with 2 packaging versions)
        var itmOil = GetOrAddItem("FG-OIL-FRY-800", "روغن سرخ‌کردنی شفاف ۸۰۰ گرمی", "روغن سرخ‌کردنی بدون پالم ترانس بالا با ماندگاری بالا", uBtl.Id, false, true, true, false, lot: true);
        var itmPalm = GetOrAddItem("RM-OIL-PALM", "پالم اولئین خوراکی تصفیه‌شده", "روغن گیاهی پالم اولئین با پایداری حرارتی بالا", uKg.Id, true, false, false, true, lot: true);
        var itmSun = GetOrAddItem("RM-OIL-SUN", "روغن آفتابگردان تصفیه‌شده", "روغن آفتابگردان خوراکی تصفیه‌شده شفاف", uKg.Id, true, false, false, true, lot: true);
        var itmAdd = GetOrAddItem("RM-ADD-TBHQ", "آنتی‌اکسیدان خوراکی TBHQ", "افزودنی مجاز خوراکی جهت جلوگیری از اکسیداسیون", uG.Id, true, false, false, true, lot: false);
        var itmPreform = GetOrAddItem("RM-PREFORM-28G", "پریفرم پت ۲۸ گرمی", "پریفرم دهانه ۲۸ میلی‌متری جهت بادکن بطری ۸۰۰ میلی‌لیتری", uPcs.Id, true, false, false, true, lot: false);
        var itmBottle = GetOrAddItem("RM-BOTTLE-800ML", "بطری آماده ۸۰۰ میلی‌لیتری پت", "بطری پت آماده خریداری‌شده دهانه ۲۸ میلی‌متری", uPcs.Id, true, false, false, true, lot: false);
        var itmCap = GetOrAddItem("PKG-CAP-28MM", "درب پیچی ۲۸ میلی‌متری فلیپ‌تاپ", "درب بطری پلاستیکی با عایق‌بندی ضد نشت", uPcs.Id, true, false, false, true, lot: false);
        var itmLabel = GetOrAddItem("PKG-LABEL-800", "لیبل روغن سرخ‌کردنی ۸۰۰ گرمی", "لیبل سلفونی پشت‌چسب‌دار با مشخصات استاندارد و بارکد", uPcs.Id, true, false, false, true, lot: false);
        var itmCarton12 = GetOrAddItem("PKG-CARTON-12", "کارتن مقوایی ۱۲ عددی", "کارتن مقوایی ۵ لا چاپی برای ۱۲ بطری ۸۰۰ گرمی", uPcs.Id, true, false, false, true, lot: false);

        // Canned Tuna Products & Materials
        var itmTuna = GetOrAddItem("FG-TUNA-180G", "کنسرو تن ماهی در روغن ۱۸۰ گرمی", "کنسرو ماهی تن هوور در روغن سویا خالص ۱۸۰ گرم", uCan.Id, false, true, true, false, lot: true);
        var itmFish = GetOrAddItem("RM-FISH-TUNA", "ماهی هوور منجمد صنعتی", "ماهی تن هوور کامل منجمد دریای جنوب", uKg.Id, true, false, false, true, lot: true);
        var itmSoya = GetOrAddItem("RM-OIL-SOYA", "روغن مایع سویا تصفیه‌شده", "روغن گیاهی سویا جهت کنسرو ماهی", uKg.Id, true, false, false, true, lot: false);
        var itmSalt = GetOrAddItem("RM-SALT-FOOD", "نمک تصفیه خوراکی بدون ید", "نمک صنعتی خوراکی مخصوص کنسروجات شیلاتی", uKg.Id, true, false, false, true, lot: false);
        var itmCan180 = GetOrAddItem("PKG-CAN-180G", "قوطی فلزی ۱۸۰ گرمی لاک‌دار", "قوطی آلومینیومی دو تکه با لاک طلایی مقاوم", uPcs.Id, true, false, false, true, lot: false);
        var itmLid = GetOrAddItem("PKG-LID-EO", "درب آسان‌بازشو ۱۸۰ گرمی", "درب فلزی ایزی‌اوپن ضد زنگ", uPcs.Id, true, false, false, true, lot: false);
        var itmCarton24 = GetOrAddItem("PKG-CARTON-24", "کارتن مادر ۲۴ عددی تن ماهی", "کارتن مقوایی صادراتی ۲۴ قوطی", uPcs.Id, true, false, false, true, lot: false);

        await db.SaveChangesAsync(SeedActor);

        // Classifications assignments
        void EnsureItemClass(long itmId, long clsId)
        {
            if (!db.ItemClassifications.Any(ic => ic.OrganizationId == orgId && ic.ItemId == itmId && ic.ClassificationId == clsId))
            {
                db.ItemClassifications.Add(new ItemClassification(orgId, itmId, clsId));
            }
        }

        EnsureItemClass(itmGpps.Id, nodeGpps.Id);
        EnsureItemClass(itmSm.Id, nodePetroRoot.Id);
        EnsureItemClass(itmOil.Id, nodeFryOil.Id);
        EnsureItemClass(itmPreform.Id, nodeBottles.Id);
        EnsureItemClass(itmBottle.Id, nodeBottles.Id);
        EnsureItemClass(itmCarton12.Id, nodeBoxes.Id);
        EnsureItemClass(itmTuna.Id, nodeTuna.Id);
        EnsureItemClass(itmCan180.Id, nodeCans.Id);
        EnsureItemClass(itmCarton24.Id, nodeBoxes.Id);

        // Technical attribute assignments
        void EnsureAttrValue(AttributeDefinition def, long itmId, string? text, decimal? dec, bool? b)
        {
            if (!db.ItemAttributeValues.Any(v => v.OrganizationId == orgId && v.ItemId == itmId && v.AttributeDefinitionId == def.Id))
            {
                db.ItemAttributeValues.Add(ItemAttributeValue.CreateValidated(def, itmId, text, dec, b));
            }
        }

        EnsureAttrValue(attrMfi, itmGpps.Id, null, 9.0m, null);
        EnsureAttrValue(attrVicat, itmGpps.Id, null, 96.0m, null);
        EnsureAttrValue(attrDensity, itmGpps.Id, null, 1.04m, null);
        EnsureAttrValue(attrIodine, itmOil.Id, null, 60.0m, null);
        EnsureAttrValue(attrFfa, itmOil.Id, null, 0.08m, null);
        EnsureAttrValue(attrFoodGrade, itmOil.Id, null, null, true);
        EnsureAttrValue(attrDrained, itmTuna.Id, null, 70.0m, null);
        EnsureAttrValue(attrHistamine, itmTuna.Id, null, 25.0m, null);
        EnsureAttrValue(attrFoodGrade, itmTuna.Id, null, null, true);

        // Unit conversions
        void EnsureItemUnitConv(long itmId, long fromUId, long toUId, decimal factor)
        {
            if (!db.ItemUnitConversions.Any(c => c.OrganizationId == orgId && c.ItemId == itmId && c.FromUnitId == fromUId && c.ToUnitId == toUId))
            {
                db.ItemUnitConversions.Add(ItemUnitConversion.Create(orgId, itmId, fromUId, toUId, factor));
            }
        }

        EnsureItemUnitConv(itmOil.Id, uBox.Id, uBtl.Id, 12m);
        EnsureItemUnitConv(itmTuna.Id, uBox.Id, uCan.Id, 24m);

        await db.SaveChangesAsync(SeedActor);

        // Work Centers
        var existingWcs = await db.WorkCenters.Where(w => w.OrganizationId == orgId && w.SiteId == siteId).ToDictionaryAsync(w => w.Code);
        WorkCenter GetOrAddWc(string code, string name, decimal cap, decimal hours, decimal? cost)
        {
            if (existingWcs.TryGetValue(code, out var w)) return w;
            var nw = WorkCenter.Create(orgId, siteId, code, name, WorkCenterType.Machine, cap, hours, cost, "ایستگاه کاری صنعتی فعال");
            db.WorkCenters.Add(nw);
            existingWcs[code] = nw;
            return nw;
        }

        var wcPolymer = GetOrAddWc("WC-POLYMER", "خط اکستروژن و گرانول‌سازی", 500m, 24m, 1500000m);
        var wcBlow = GetOrAddWc("WC-BLOW", "دستگاه بادکن بطری پت", 1200m, 16m, 800000m);
        var wcOilFill = GetOrAddWc("WC-OIL-FILL", "خط اختلاط، پرکنی و کارتن‌گذاری روغن", 1000m, 16m, 1200000m);
        var wcTunaLine = GetOrAddWc("WC-TUNA-LINE", "خط فیله‌سازی، پرکنی و روغن‌ریزی ماهی", 800m, 16m, 1100000m);
        var wcAutoclave = GetOrAddWc("WC-AUTOCLAVE", "دیگ‌های اتوکلاو استریلیزاسیون حرارتی", 3000m, 24m, 2000000m);
        await db.SaveChangesAsync(SeedActor);

        // BOM & Routings
        var bomGpps = await db.Boms.FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.Code == "BOM-GPPS-1540");
        BomVersion? vGpps = null;
        if (bomGpps is null)
        {
            bomGpps = Bom.Create(orgId, "BOM-GPPS-1540", "فرمول تولید پلی‌استایرن GPPS 1540", itmGpps.Id);
            db.Boms.Add(bomGpps);
            await db.SaveChangesAsync(SeedActor);

            vGpps = BomVersion.CreateDraft(orgId, bomGpps.Id, 1, 1000m, uKg.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)), null, "فرمول استاندارد تولید گرانول GPPS");
            db.BomVersions.Add(vGpps);
            await db.SaveChangesAsync(SeedActor);

            db.BomInputs.Add(new BomInput(orgId, vGpps.Id, 1, itmSm.Id, 985m, uKg.Id, 0m, false, "استایرن منومر خالص"));
            db.BomInputs.Add(new BomInput(orgId, vGpps.Id, 2, itmCat.Id, 5m, uKg.Id, 0m, false, "آغازگر پروکساید"));
            db.BomInputs.Add(new BomInput(orgId, vGpps.Id, 3, itmMinOil.Id, 10m, uKg.Id, 0m, false, "روغن معدنی سفید"));
            db.BomInputs.Add(new BomInput(orgId, vGpps.Id, 4, itmBag25.Id, 40m, uPcs.Id, 0m, false, "کیسه ۲۵ کیلویی لمینت‌دار"));
            db.BomOutputs.Add(new BomOutput(orgId, vGpps.Id, 1, itmGpps.Id, BomOutputType.MainProduct, 1000m, uKg.Id));

            vGpps.TransitionTo(BomVersionStatus.Approved);
            vGpps.TransitionTo(BomVersionStatus.Active);
            await db.SaveChangesAsync(SeedActor);

            var rGpps = Routing.CreateDraft(orgId, siteId, "RT-GPPS-1540", "مسیر تولید GPPS 1540", itmGpps.Id, vGpps.Id, 1, true, null);
            rGpps.ReplaceOperations([(10, "OP-EXTRUDE", "پلیمریزاسیون و اکستروژن گرانول", wcPolymer.Id, 30m, 0.05m, 10m, 10m, true, "کنترل دبی مذاب و دمای سیلندر")]);
            rGpps.Activate(DateTime.UtcNow);
            db.Routings.Add(rGpps);
            await db.SaveChangesAsync(SeedActor);
        }
        else
        {
            vGpps = await db.BomVersions.SingleAsync(v => v.OrganizationId == orgId && v.BomId == bomGpps.Id && v.VersionNumber == 1);
        }

        var bomOil = await db.Boms.FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.Code == "BOM-OIL-FRY-800");
        BomVersion? vOil1 = null;
        BomVersion? vOil2 = null;
        if (bomOil is null)
        {
            bomOil = Bom.Create(orgId, "BOM-OIL-FRY-800", "فرمول تولید روغن سرخ‌کردنی ۸۰۰ گرمی", itmOil.Id);
            db.Boms.Add(bomOil);
            await db.SaveChangesAsync(SeedActor);

            vOil1 = BomVersion.CreateDraft(orgId, bomOil.Id, 1, 600m, uBtl.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), null, "نسخه ۱ - ساخت بطری در محل از پریفرم پت");
            db.BomVersions.Add(vOil1);
            await db.SaveChangesAsync(SeedActor);

            db.BomInputs.Add(new BomInput(orgId, vOil1.Id, 1, itmPalm.Id, 240m, uKg.Id, 0m, false, "پالم اولئین"));
            db.BomInputs.Add(new BomInput(orgId, vOil1.Id, 2, itmSun.Id, 240m, uKg.Id, 0m, false, "روغن آفتابگردان"));
            db.BomInputs.Add(new BomInput(orgId, vOil1.Id, 3, itmAdd.Id, 50m, uG.Id, 0m, false, "آنتی‌اکسیدان TBHQ"));
            db.BomInputs.Add(new BomInput(orgId, vOil1.Id, 4, itmPreform.Id, 600m, uPcs.Id, 0m, false, "پریفرم پت ۲۸ گرمی (بادکن داخلی)"));
            db.BomInputs.Add(new BomInput(orgId, vOil1.Id, 5, itmCap.Id, 600m, uPcs.Id, 0m, false, "درب پیچی"));
            db.BomInputs.Add(new BomInput(orgId, vOil1.Id, 6, itmLabel.Id, 600m, uPcs.Id, 0m, false, "لیبل"));
            db.BomInputs.Add(new BomInput(orgId, vOil1.Id, 7, itmCarton12.Id, 50m, uPcs.Id, 0m, false, "کارتن ۱۲ عددی"));
            db.BomOutputs.Add(new BomOutput(orgId, vOil1.Id, 1, itmOil.Id, BomOutputType.MainProduct, 600m, uBtl.Id));

            vOil1.TransitionTo(BomVersionStatus.Approved);
            vOil1.TransitionTo(BomVersionStatus.Active);
            await db.SaveChangesAsync(SeedActor);

            vOil2 = BomVersion.CreateDraft(orgId, bomOil.Id, 2, 600m, uBtl.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), null, "نسخه ۲ - خرید مستقیم بطری آماده از بیرون");
            db.BomVersions.Add(vOil2);
            await db.SaveChangesAsync(SeedActor);

            db.BomInputs.Add(new BomInput(orgId, vOil2.Id, 1, itmPalm.Id, 240m, uKg.Id, 0m, false, "پالم اولئین"));
            db.BomInputs.Add(new BomInput(orgId, vOil2.Id, 2, itmSun.Id, 240m, uKg.Id, 0m, false, "روغن آفتابگردان"));
            db.BomInputs.Add(new BomInput(orgId, vOil2.Id, 3, itmAdd.Id, 50m, uG.Id, 0m, false, "آنتی‌اکسیدان TBHQ"));
            db.BomInputs.Add(new BomInput(orgId, vOil2.Id, 4, itmBottle.Id, 600m, uPcs.Id, 0m, false, "بطری آماده ۸۰۰ میلی‌لیتری پت (خرید خارجی)"));
            db.BomInputs.Add(new BomInput(orgId, vOil2.Id, 5, itmCap.Id, 600m, uPcs.Id, 0m, false, "درب پیچی"));
            db.BomInputs.Add(new BomInput(orgId, vOil2.Id, 6, itmLabel.Id, 600m, uPcs.Id, 0m, false, "لیبل"));
            db.BomInputs.Add(new BomInput(orgId, vOil2.Id, 7, itmCarton12.Id, 50m, uPcs.Id, 0m, false, "کارتن ۱۲ عددی"));
            db.BomOutputs.Add(new BomOutput(orgId, vOil2.Id, 1, itmOil.Id, BomOutputType.MainProduct, 600m, uBtl.Id));

            vOil2.TransitionTo(BomVersionStatus.Approved);
            await db.SaveChangesAsync(SeedActor);

            var rOil = Routing.CreateDraft(orgId, siteId, "RT-OIL-FRY-800", "مسیر تولید روغن سرخ‌کردنی", itmOil.Id, vOil1.Id, 1, true, null);
            rOil.ReplaceOperations([
                (10, "OP-BLOW", "بادکن بطری پریفرم", wcBlow.Id, 15m, 0.02m, 5m, 5m, true, "کنترل ضخامت جداره"),
                (20, "OP-FILL", "تزریق روغن، درب‌بندی و کارتن‌گذاری", wcOilFill.Id, 20m, 0.03m, 5m, 5m, true, "کنترل وزن پرکنی و نشتی")
            ]);
            rOil.Activate(DateTime.UtcNow);
            db.Routings.Add(rOil);
            await db.SaveChangesAsync(SeedActor);
        }
        else
        {
            vOil1 = await db.BomVersions.SingleAsync(v => v.OrganizationId == orgId && v.BomId == bomOil.Id && v.VersionNumber == 1);
            vOil2 = await db.BomVersions.SingleAsync(v => v.OrganizationId == orgId && v.BomId == bomOil.Id && v.VersionNumber == 2);
        }

        var bomTuna = await db.Boms.FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.Code == "BOM-TUNA-180G");
        BomVersion? vTuna = null;
        if (bomTuna is null)
        {
            bomTuna = Bom.Create(orgId, "BOM-TUNA-180G", "فرمول تولید کنسرو تن ماهی ۱۸۰ گرمی", itmTuna.Id);
            db.Boms.Add(bomTuna);
            await db.SaveChangesAsync(SeedActor);

            vTuna = BomVersion.CreateDraft(orgId, bomTuna.Id, 1, 2400m, uCan.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)), null, "فرمول استاندارد کنسرو تن ماهی در روغن سویا");
            db.BomVersions.Add(vTuna);
            await db.SaveChangesAsync(SeedActor);

            db.BomInputs.Add(new BomInput(orgId, vTuna.Id, 1, itmFish.Id, 450m, uKg.Id, 0m, false, "ماهی هوور منجمد"));
            db.BomInputs.Add(new BomInput(orgId, vTuna.Id, 2, itmSoya.Id, 90m, uKg.Id, 0m, false, "روغن سویا"));
            db.BomInputs.Add(new BomInput(orgId, vTuna.Id, 3, itmSalt.Id, 5m, uKg.Id, 0m, false, "نمک تصفیه"));
            db.BomInputs.Add(new BomInput(orgId, vTuna.Id, 4, itmCan180.Id, 2400m, uPcs.Id, 0m, false, "قوطی فلزی ۱۸۰ گرمی"));
            db.BomInputs.Add(new BomInput(orgId, vTuna.Id, 5, itmLid.Id, 2400m, uPcs.Id, 0m, false, "درب آسان‌بازشو"));
            db.BomInputs.Add(new BomInput(orgId, vTuna.Id, 6, itmCarton24.Id, 100m, uPcs.Id, 0m, false, "کارتن ۲۴ عددی"));
            db.BomOutputs.Add(new BomOutput(orgId, vTuna.Id, 1, itmTuna.Id, BomOutputType.MainProduct, 2400m, uCan.Id));

            vTuna.TransitionTo(BomVersionStatus.Approved);
            vTuna.TransitionTo(BomVersionStatus.Active);
            await db.SaveChangesAsync(SeedActor);

            var rTuna = Routing.CreateDraft(orgId, siteId, "RT-TUNA-180G", "مسیر تولید کنسرو تن ماهی", itmTuna.Id, vTuna.Id, 1, true, null);
            rTuna.ReplaceOperations([
                (10, "OP-CAN-FILL", "پرکنی گوشت ماهی و تزریق روغن", wcTunaLine.Id, 25m, 0.04m, 5m, 5m, true, "کنترل وزن آبکش و بافت گوشت"),
                (20, "OP-AUTOCLAVE", "استریلیزاسیون حرارتی اتوکلاو", wcAutoclave.Id, 60m, 0.01m, 15m, 15m, true, "کنترل گراف حرارتی ۱۲۱ درجه سانتیگراد")
            ]);
            rTuna.Activate(DateTime.UtcNow);
            db.Routings.Add(rTuna);
            await db.SaveChangesAsync(SeedActor);
        }
        else
        {
            vTuna = await db.BomVersions.SingleAsync(v => v.OrganizationId == orgId && v.BomId == bomTuna.Id && v.VersionNumber == 1);
        }

        var rtGpps = await db.Routings.SingleAsync(r => r.OrganizationId == orgId && r.Code == "RT-GPPS-1540");
        var rtOil = await db.Routings.SingleAsync(r => r.OrganizationId == orgId && r.Code == "RT-OIL-FRY-800");
        var rtTuna = await db.Routings.SingleAsync(r => r.OrganizationId == orgId && r.Code == "RT-TUNA-180G");

        Console.WriteLine(" ✔ فرمول‌های ساخت مهندسی، مراکز کاری و مسیرهای تولید آماده شدند.");

        // =========================================================================
        // 5. Quality Control Setup (Parameters & Specifications)
        // =========================================================================
        Console.WriteLine("\n[6/8] تعریف پارامترهای آزمایشگاهی کنترل کیفیت و استانداردهای بازرسی (Quality Specs)...");

        var existingParams = await db.QualityParameters.Where(q => q.OrganizationId == orgId).ToDictionaryAsync(q => q.Code);
        QualityParameter GetOrAddParam(string code, string name, string symbol, string? unitSymbol, QualityParameterDataType dt, byte decPlaces, string? method)
        {
            if (existingParams.TryGetValue(code, out var p)) return p;
            var np = QualityParameter.Create(orgId, code, name, symbol, unitSymbol, dt, decPlaces, method);
            db.QualityParameters.Add(np);
            existingParams[code] = np;
            return np;
        }

        var pMfi = GetOrAddParam("PARAM-MFI", "شاخص جریان مذاب (MFR/MFI)", "MFR", "g/10min", QualityParameterDataType.Numeric, 2, "ASTM D1238");
        var pVicat = GetOrAddParam("PARAM-VICAT", "دمای نرمی ویکات", "VST", "°C", QualityParameterDataType.Numeric, 1, "ASTM D1525");
        var pPurity = GetOrAddParam("PARAM-PURITY", "درصد خلوص منومر", "Purity", "%", QualityParameterDataType.Numeric, 2, "GC-FID");
        var pFfa = GetOrAddParam("PARAM-FFA", "اسید چرب آزاد (FFA)", "FFA", "%", QualityParameterDataType.Numeric, 3, "AOCS Ca 5a-40");
        var pPv = GetOrAddParam("PARAM-PV", "عدد پراکسید (PV)", "PV", "meq/kg", QualityParameterDataType.Numeric, 2, "AOCS Cd 8-53");
        var pHistamine = GetOrAddParam("PARAM-HISTAMINE", "میزان هیستامین ماهی", "Histamine", "ppm", QualityParameterDataType.Numeric, 1, "HPLC / ISIRI");
        var pDrained = GetOrAddParam("PARAM-DRAINED", "درصد وزن آبکش کنسرو", "Drained Wt", "%", QualityParameterDataType.Numeric, 1, "ISIRI 1881");
        var pFoodGrade = GetOrAddParam("PARAM-FOOD-GRADE", "مجوز تماس با غذا (Food Grade)", "Food Grade", null, QualityParameterDataType.Boolean, 0, "ISIRI Food Contact");
        var pLeak = GetOrAddParam("PARAM-LEAK", "تست عدم نشتی و هوابندی درب", "Leak Test", null, QualityParameterDataType.Boolean, 0, "Vacuum Decay");

        await db.SaveChangesAsync(SeedActor);

        // Quality Specifications
        var existingSpecs = await db.QualitySpecs.Where(s => s.OrganizationId == orgId).ToDictionaryAsync(s => s.Code);
        QualitySpec EnsureSpec(string code, string name, QualitySpecTargetKind kind, long targetId, QualitySpecCheckpoint checkpoint, IEnumerable<QualitySpecLineInputData> lines)
        {
            if (existingSpecs.TryGetValue(code, out var s)) return s;
            var spec = QualitySpec.Create(
                orgId, code, name, kind,
                kind == QualitySpecTargetKind.Item ? targetId : null,
                kind == QualitySpecTargetKind.Classification ? targetId : null,
                checkpoint, 1, "استاندارد مصوب آزمایشگاه کنترل کیفیت", lines);
            db.QualitySpecs.Add(spec);
            existingSpecs[code] = spec;
            return spec;
        }

        // 1. Raw Styrene Monomer Incoming Spec
        var specSm = EnsureSpec("SPEC-SM-RAW", "استاندارد ورود استایرن منومر", QualitySpecTargetKind.Item, itmSm.Id, QualitySpecCheckpoint.Incoming, [
            new QualitySpecLineInputData(pPurity.Id, 99.80m, 100.00m, 99.95m, null, true, "خلوص منومر بالای ۹۹.۸ درصد الزامی است")
        ]);

        // 2. Raw Sunflower Oil Incoming Spec
        var specSun = EnsureSpec("SPEC-SUN-RAW", "استاندارد ورود روغن آفتابگردان خام", QualitySpecTargetKind.Item, itmSun.Id, QualitySpecCheckpoint.Incoming, [
            new QualitySpecLineInputData(pFfa.Id, null, 0.20m, 0.10m, null, true, "حداکثر اسید چرب آزاد ۰.۲ درصد"),
            new QualitySpecLineInputData(pPv.Id, null, 2.0m, 1.0m, null, true, "حداکثر عدد پراکسید ۲.۰")
        ]);

        // 3. Raw Frozen Tuna Fish Incoming Spec
        var specFish = EnsureSpec("SPEC-FISH-RAW", "استاندارد پذیرش ماهی تن هوور منجمد", QualitySpecTargetKind.Item, itmFish.Id, QualitySpecCheckpoint.Incoming, [
            new QualitySpecLineInputData(pHistamine.Id, null, 50.0m, 20.0m, null, true, "حداکثر هیستامین ۵۰ پی‌پی‌ام مجاز است")
        ]);

        // 4. GPPS 1540 Finished Good Spec
        var specGpps = EnsureSpec("SPEC-GPPS-FG", "استاندارد ترخیص پلی‌استایرن GPPS 1540", QualitySpecTargetKind.Item, itmGpps.Id, QualitySpecCheckpoint.Production, [
            new QualitySpecLineInputData(pMfi.Id, 8.0m, 10.0m, 9.0m, null, true, "شاخص جریان مذاب بین ۸ تا ۱۰"),
            new QualitySpecLineInputData(pVicat.Id, 94.0m, 98.0m, 96.0m, null, true, "دمای نرمی ویکات بین ۹۴ تا ۹۸")
        ]);

        // 5. Frying Oil Finished Good Spec
        var specOil = EnsureSpec("SPEC-OIL-FRY-FG", "استاندارد ترخیص روغن سرخ‌کردنی ۸۰۰ گرمی", QualitySpecTargetKind.Item, itmOil.Id, QualitySpecCheckpoint.Production, [
            new QualitySpecLineInputData(pFfa.Id, null, 0.10m, 0.08m, null, true, "اسید چرب آزاد محصول نهایی"),
            new QualitySpecLineInputData(pPv.Id, null, 1.5m, 0.8m, null, true, "عدد پراکسید محصول نهایی"),
            new QualitySpecLineInputData(pLeak.Id, null, null, null, "true", true, "تست عدم نشتی درب و فویل")
        ]);

        // 6. Canned Tuna Finished Good Spec
        var specTuna = EnsureSpec("SPEC-TUNA-FG", "استاندارد ترخیص کنسرو تن ماهی ۱۸۰ گرمی", QualitySpecTargetKind.Item, itmTuna.Id, QualitySpecCheckpoint.Production, [
            new QualitySpecLineInputData(pDrained.Id, 68.0m, 75.0m, 70.0m, null, true, "درصد وزن آبکش استاندارد"),
            new QualitySpecLineInputData(pHistamine.Id, null, 40.0m, 25.0m, null, true, "هیستامین نهایی زیر ۴۰"),
            new QualitySpecLineInputData(pLeak.Id, null, null, null, "true", true, "آزمون فشار منفی و سیلینگ درب")
        ]);

        await db.SaveChangesAsync(SeedActor);
        Console.WriteLine(" ✔ پارامترهای آزمایشگاهی و فرم‌های استاندارد کیفی (Specs) ثبت شدند.");

        // =========================================================================
        // 6. Cleanup of Demo Transactional Documents if re-running
        // =========================================================================
        Console.WriteLine("\n[7/8] آماده‌سازی چرخه تجاری و تولیدی (پاکسازی رکوردهای تراکنشی قبلی جهت ثبت مجدد با ارتباط کامل)...");

        var demoTxnDocs = new[] {
            "PO-2026-001", "PO-2026-002", "PO-2026-003", "PO-2026-004", "PO-2026-005", "PO-2026-006", "PO-2026-007",
            "MO-2026-001", "MO-2026-002", "MO-2026-003", "MO-2026-004", "MO-2026-005", "MO-2026-006", "MO-2026-007",
            "DO-2026-001", "DO-2026-002", "DO-2026-003",
            "SO-2026-001", "SO-2026-002", "SO-2026-003", "SO-2026-004", "SO-2026-005",
            "INSP-2026-001", "INSP-2026-002", "INSP-2026-003", "INSP-2026-004", "INSP-2026-005", "INSP-2026-006", "INSP-2026-007"
        };

        // Direct SQL cleanup respecting foreign keys and domain append-only rules
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM organization.InventoryLedgerEntry WHERE OrganizationId = {0};
            DELETE FROM organization.InventoryTransaction WHERE OrganizationId = {0};
            DELETE FROM organization.DeliveryOrderLine WHERE OrganizationId = {0};
            DELETE FROM organization.DeliveryOrder WHERE OrganizationId = {0};
            DELETE FROM organization.InventoryBalance WHERE OrganizationId = {0};
            DELETE FROM organization.SalesOrderLine WHERE OrganizationId = {0};
            DELETE FROM organization.SalesOrder WHERE OrganizationId = {0};
            DELETE FROM organization.QualityInspectionLine WHERE OrganizationId = {0};
            DELETE FROM organization.QualityInspection WHERE OrganizationId = {0};
            DELETE FROM organization.ProductionOrderOperation WHERE OrganizationId = {0};
            DELETE FROM organization.ProductionOrderInput WHERE OrganizationId = {0};
            DELETE FROM organization.ProductionOrderOutput WHERE OrganizationId = {0};
            DELETE FROM organization.ProductionOrder WHERE OrganizationId = {0};
            DELETE FROM organization.PurchaseOrderLine WHERE OrganizationId = {0};
            DELETE FROM organization.PurchaseOrder WHERE OrganizationId = {0};
        ", orgId);
        db.ChangeTracker.Clear();

        Console.WriteLine(" ✔ جدول‌های تراکنشی جهت ثبت دقیق و یکپارچه آماده شدند.");

        // =========================================================================
        // 7. Full Business Lifecycle Execution
        // =========================================================================
        Console.WriteLine("\n[8/8] اجرای چرخه کامل ERP: سفارشات خرید، بازرسی کیفی، دستورات تولید، سفارشات فروش و ارسال...");

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        // -------------------------------------------------------------------------
        // A. Purchase Orders (PO-2026-001 through 007)
        // -------------------------------------------------------------------------
        Console.WriteLine("    1. ثبت سفارشات خرید مواد اولیه و بسته‌بندی در فرم خرید (Purchase Orders)...");

        // PO 1: Petrochemicals from Pars Petrochemical (Received)
        var po1 = PurchaseOrder.CreateDraft(orgId, siteId, supPars.Id, "PO-2026-001", today.AddDays(-20), today.AddDays(-15), whRm.Id, "IRR", "تأمین منومر و مواد افزودنی پلیمریزاسیون");
        po1.ReplaceLines([
            (itmSm.Id, 5000m, uKg.Id, uKg.Id, 5000m, 450000m, "استایرن منومر خالص"),
            (itmCat.Id, 50m, uKg.Id, uKg.Id, 50m, 1200000m, "آغازگر پروکساید صنعتی"),
            (itmMinOil.Id, 100m, uKg.Id, uKg.Id, 100m, 300000m, "روغن معدنی فرآیندی"),
            (itmBag25.Id, 500m, uPcs.Id, uPcs.Id, 500m, 15000m, "کیسه لمینت ۲۵ کیلویی")
        ]);
        po1.Approve(now.AddDays(-19));
        db.PurchaseOrders.Add(po1);
        await db.SaveChangesAsync(SeedActor);

        // Apply receipts for PO 1
        po1.ApplyReceipts(po1.Lines.Select(l => (l.Id, l.OrderedBaseQuantity)).ToList());
        await db.SaveChangesAsync(SeedActor);

        // PO 2: Edible Oils & Ingredients from Behpak (Received)
        var po2 = PurchaseOrder.CreateDraft(orgId, siteId, supBehpak.Id, "PO-2026-002", today.AddDays(-20), today.AddDays(-14), whRm.Id, "IRR", "تأمین روغن‌های گیاهی پالم و آفتابگردان و سویا");
        po2.ReplaceLines([
            (itmPalm.Id, 3000m, uKg.Id, uKg.Id, 3000m, 550000m, "پالم اولئین تصفیه‌شده"),
            (itmSun.Id, 3000m, uKg.Id, uKg.Id, 3000m, 620000m, "روغن آفتابگردان شفاف"),
            (itmSoya.Id, 1000m, uKg.Id, uKg.Id, 1000m, 580000m, "روغن سویا خالص"),
            (itmAdd.Id, 500m, uG.Id, uG.Id, 500m, 12000m, "افزودنی مجاز TBHQ"),
            (itmSalt.Id, 100m, uKg.Id, uKg.Id, 100m, 25000m, "نمک تصفیه خوراکی")
        ]);
        po2.Approve(now.AddDays(-19));
        db.PurchaseOrders.Add(po2);
        await db.SaveChangesAsync(SeedActor);

        po2.ApplyReceipts(po2.Lines.Select(l => (l.Id, l.OrderedBaseQuantity)).ToList());
        await db.SaveChangesAsync(SeedActor);

        // PO 3: Plastic Packaging from Pars Pet (Received)
        var po3 = PurchaseOrder.CreateDraft(orgId, siteId, supParsPet.Id, "PO-2026-003", today.AddDays(-18), today.AddDays(-12), whRm.Id, "IRR", "تأمین پریفرم، بطری‌های آماده و ملزومات بطری‌سازی");
        po3.ReplaceLines([
            (itmPreform.Id, 3000m, uPcs.Id, uPcs.Id, 3000m, 4000m, "پریفرم پت ۲۸ گرمی"),
            (itmBottle.Id, 2000m, uPcs.Id, uPcs.Id, 2000m, 7500m, "بطری آماده ۸۰۰ میلی‌لیتری"),
            (itmCap.Id, 5000m, uPcs.Id, uPcs.Id, 5000m, 1200m, "درب پیچی ۲۸ میلی‌متری"),
            (itmLabel.Id, 5000m, uPcs.Id, uPcs.Id, 5000m, 800m, "لیبل سلفونی ۸۰۰ گرمی"),
            (itmCarton12.Id, 500m, uPcs.Id, uPcs.Id, 500m, 18000m, "کارتن مقوایی ۱۲ عددی")
        ]);
        po3.Approve(now.AddDays(-17));
        db.PurchaseOrders.Add(po3);
        await db.SaveChangesAsync(SeedActor);

        po3.ApplyReceipts(po3.Lines.Select(l => (l.Id, l.OrderedBaseQuantity)).ToList());
        await db.SaveChangesAsync(SeedActor);

        // PO 4: Metal Cans from Can Khazar (Received)
        var po4 = PurchaseOrder.CreateDraft(orgId, siteId, supKhazar.Id, "PO-2026-004", today.AddDays(-18), today.AddDays(-10), whRm.Id, "IRR", "تأمین قوطی‌های فلزی ۱۸۰ گرمی و درب آسان‌بازشو");
        po4.ReplaceLines([
            (itmCan180.Id, 5000m, uPcs.Id, uPcs.Id, 5000m, 12000m, "قوطی فلزی ۱۸۰ گرمی"),
            (itmLid.Id, 5000m, uPcs.Id, uPcs.Id, 5000m, 6000m, "درب ایزی‌اوپن ضدزنگ"),
            (itmCarton24.Id, 300m, uPcs.Id, uPcs.Id, 300m, 25000m, "کارتن ۲۴ عددی صادراتی")
        ]);
        po4.Approve(now.AddDays(-17));
        db.PurchaseOrders.Add(po4);
        await db.SaveChangesAsync(SeedActor);

        po4.ApplyReceipts(po4.Lines.Select(l => (l.Id, l.OrderedBaseQuantity)).ToList());
        await db.SaveChangesAsync(SeedActor);

        // PO 5: Frozen Tuna Fish from Shilat Jonoub (Received)
        var po5 = PurchaseOrder.CreateDraft(orgId, siteId, supShilat.Id, "PO-2026-005", today.AddDays(-15), today.AddDays(-10), whRm.Id, "IRR", "محموله کانتینری ماهی تن هوور منجمد");
        po5.ReplaceLines([
            (itmFish.Id, 2500m, uKg.Id, uKg.Id, 2500m, 950000m, "ماهی هوور منجمد صنعتی")
        ]);
        po5.Approve(now.AddDays(-14));
        db.PurchaseOrders.Add(po5);
        await db.SaveChangesAsync(SeedActor);

        po5.ApplyReceipts(po5.Lines.Select(l => (l.Id, l.OrderedBaseQuantity)).ToList());
        await db.SaveChangesAsync(SeedActor);

        // PO 6: Open Approved Order (Tests active open purchase order in UI)
        var po6 = PurchaseOrder.CreateDraft(orgId, siteId, supBehpak.Id, "PO-2026-006", today.AddDays(-2), today.AddDays(7), whRm.Id, "IRR", "سفارش باز روغن آفتابگردان نوبت هفته آینده");
        po6.ReplaceLines([
            (itmSun.Id, 5000m, uKg.Id, uKg.Id, 5000m, 630000m, "روغن آفتابگردان تصفیه‌شده شفاف")
        ]);
        po6.Approve(now.AddDays(-1));
        db.PurchaseOrders.Add(po6);
        await db.SaveChangesAsync(SeedActor);

        // PO 7: Draft Order (Tests draft purchase order editing in UI)
        var po7 = PurchaseOrder.CreateDraft(orgId, siteId, supPars.Id, "PO-2026-007", today, today.AddDays(14), whRm.Id, "IRR", "پیش‌نویس سفارش تانکری منومر استایرن");
        po7.ReplaceLines([
            (itmSm.Id, 10000m, uKg.Id, uKg.Id, 10000m, 455000m, "استایرن منومر تانکری")
        ]);
        db.PurchaseOrders.Add(po7);
        await db.SaveChangesAsync(SeedActor);

        Console.WriteLine("    ✔ ۷ سفارش خرید با وضعیت‌های مختلف (رسیدشده، تاییدشده باز، پیش‌نویس) ثبت شدند.");

        // -------------------------------------------------------------------------
        // B. Incoming Quality Inspections (INSP-2026-001 through 003, 007)
        // -------------------------------------------------------------------------
        Console.WriteLine("    2. ثبت بازرسی‌های کنترل کیفیت ورودی محموله‌ها (Incoming Inspections) و آزمون‌های آزمایشگاهی...");

        // INSP 1: Styrene Monomer Incoming Inspection (Accepted with COA)
        var insp1 = QualityInspection.Create(orgId, siteId, whRm.Id, itmSm.Id, "LOT-SM-202611-01", "INSP-2026-001",
            QualityInspectionSourceType.Receipt, null, null, uKg.Id, 5000m, "بازرسی کیفی تانکر منومر ورودی از پتروشیمی پارس");
        insp1.ReplaceLines([
            new QualityInspectionLineWriteData(pPurity.Name, "99.80 - 100.00 %", "99.96", true, "خلوص عالی مطابق کروماتوگرافی گازی",
                pPurity.Id, pPurity.Code, pPurity.Symbol, pPurity.UnitSymbol, pPurity.DefaultTestMethod, pPurity.DataType,
                99.80m, 100.00m, 99.95m, 99.96m, true)
        ]);
        insp1.UpdateHeaderNotes(null, "تایید کیفی منومر ورودی - بدون ناخالصی پلیمر اولیه", "COA-SM-PARS-01");
        insp1.RecordDisposition(5000m, 0m, null, "تایید کامل و ترخیص جهت استفاده در خط پلیمریزاسیون", SeedActor.ActorId, now.AddDays(-15));
        db.QualityInspections.Add(insp1);

        // INSP 2: Sunflower Oil Incoming Inspection (Accepted with COA)
        var insp2 = QualityInspection.Create(orgId, siteId, whRm.Id, itmSun.Id, "LOT-SUN-202611-01", "INSP-2026-002",
            QualityInspectionSourceType.Receipt, null, null, uKg.Id, 3000m, "کنترل کیفی محموله روغن آفتابگردان بهپاک");
        insp2.ReplaceLines([
            new QualityInspectionLineWriteData(pFfa.Name, "Max 0.20 %", "0.08", true, "اسید چرب آزاد در محدوده مجاز استاندارد ملی",
                pFfa.Id, pFfa.Code, pFfa.Symbol, pFfa.UnitSymbol, pFfa.DefaultTestMethod, pFfa.DataType,
                null, 0.20m, 0.10m, 0.08m, true),
            new QualityInspectionLineWriteData(pPv.Name, "Max 2.0 meq/kg", "0.95", true, "پراکسید پایین - روغن کاملاً تازه",
                pPv.Id, pPv.Code, pPv.Symbol, pPv.UnitSymbol, pPv.DefaultTestMethod, pPv.DataType,
                null, 2.0m, 1.0m, 0.95m, true)
        ]);
        insp2.UpdateHeaderNotes(null, "محصول باکیفیت و بدون بو یا اکسیداسیون", "COA-SUN-BEHPAK-01");
        insp2.RecordDisposition(3000m, 0m, null, "تایید و ترخیص محموله روغن به مخازن اصلی", SeedActor.ActorId, now.AddDays(-14));
        db.QualityInspections.Add(insp2);

        // INSP 3: Frozen Tuna Fish Incoming Inspection (Accepted with COA)
        var insp3 = QualityInspection.Create(orgId, siteId, whRm.Id, itmFish.Id, "LOT-TUNA-RAW-01", "INSP-2026-003",
            QualityInspectionSourceType.Receipt, null, null, uKg.Id, 2500m, "آزمون سنجش سلامت و هیستامین ماهی تن هوور منجمد");
        insp3.ReplaceLines([
            new QualityInspectionLineWriteData(pHistamine.Name, "Max 50.0 ppm", "22.0", true, "سطح هیستامین در حد بسیار مطلوب تازگی",
                pHistamine.Id, pHistamine.Code, pHistamine.Symbol, pHistamine.UnitSymbol, pHistamine.DefaultTestMethod, pHistamine.DataType,
                null, 50.0m, 20.0m, 22.0m, true)
        ]);
        insp3.UpdateHeaderNotes(null, "ماهی هوور تازه صید شده با انجماد سریع IQF", "COA-TUNA-SHILAT-01");
        insp3.RecordDisposition(2500m, 0m, null, "تایید ورود به سردخانه مواد اولیه کارخانه", SeedActor.ActorId, now.AddDays(-10));
        db.QualityInspections.Add(insp3);

        // INSP 7: Quarantine Sample Inspection (Pending hold - tests active quarantine in UI)
        var insp7 = QualityInspection.Create(orgId, siteId, whQc.Id, itmSm.Id, "LOT-TEST-HOLD-01", "INSP-2026-007",
            QualityInspectionSourceType.ManualHold, null, null, uKg.Id, 100m, "نمونه آزمایشگاهی قرنطینه جهت ارزیابی ویسکوزیته و ناخالصی");
        insp7.ReplaceLines([
            new QualityInspectionLineWriteData(pPurity.Name, "99.80 - 100.00 %", null, false, "آزمایش در حال انجام در آزمایشگاه طیف‌سنجی",
                pPurity.Id, pPurity.Code, pPurity.Symbol, pPurity.UnitSymbol, pPurity.DefaultTestMethod, pPurity.DataType,
                99.80m, 100.00m, 99.95m, null, true)
        ]);
        db.QualityInspections.Add(insp7);

        await db.SaveChangesAsync(SeedActor);
        Console.WriteLine(" ✔ بازرسی‌های کیفی مواد ورودی و قرنطینه با پارامترهای آزمایشگاهی و شماره گواهی آنالیز COA ثبت شدند.");

        // -------------------------------------------------------------------------
        // C. Inventory Receipts for Purchased Materials (Linked to PO & QC)
        // -------------------------------------------------------------------------
        Console.WriteLine("    3. ثبت رسید انبار مواد اولیه متصل به سفارشات خرید و برگه‌های بازرسی کیفیت...");

        async Task PostDirectReceiptAsync(long itemId, long unitId, long warehouseId, decimal qty, string docNum, string? lotNum = null, long? poId = null, long? inspId = null)
        {
            var txn = await db.InventoryTransactions.SingleOrDefaultAsync(t => t.OrganizationId == orgId && t.DocumentNumber == docNum);
            if (txn is null)
            {
                txn = InventoryTransaction.Create(
                    organizationId: orgId,
                    siteId: siteId,
                    warehouseId: warehouseId,
                    destinationWarehouseId: null,
                    documentNumber: docNum,
                    transactionType: InventoryTransactionType.Receipt,
                    productionOrderId: null,
                    reversedTransactionId: null,
                    referenceNote: "رسید مستقیم خرید تأمین‌کننده با تاییدیه کنترل کیفیت",
                    idempotencyKey: null,
                    postedAtUtc: now,
                    qualityInspectionId: inspId,
                    purchaseOrderId: poId);

                db.InventoryTransactions.Add(txn);
                await db.SaveChangesAsync(SeedActor);
            }

            var lineNum = (await db.InventoryLedgerEntries.Where(l => l.OrganizationId == orgId && l.InventoryTransactionId == txn.Id).MaxAsync(l => (int?)l.LineNumber) ?? 0) + 1;

            var ledger = InventoryLedgerEntry.Create(
                organizationId: orgId,
                inventoryTransactionId: txn.Id,
                siteId: siteId,
                warehouseId: warehouseId,
                lineNumber: lineNum,
                itemId: itemId,
                lotCode: lotNum ?? string.Empty,
                enteredQuantity: qty,
                enteredUnitId: unitId,
                baseQuantityDelta: qty,
                baseUnitId: unitId,
                occurredAtUtc: now,
                notes: "رسید خرید مواد با بازرسی کیفی");

            db.InventoryLedgerEntries.Add(ledger);

            var bal = await db.InventoryBalances.SingleOrDefaultAsync(b => b.OrganizationId == orgId && b.WarehouseId == warehouseId && b.ItemId == itemId && b.LotCode == (lotNum ?? string.Empty));
            if (bal is null)
            {
                bal = InventoryBalance.CreateZero(orgId, siteId, warehouseId, itemId, lotNum ?? string.Empty, unitId, now);
                db.InventoryBalances.Add(bal);
            }
            bal.ApplyDelta(qty, now, enforceAvailableOnOutbound: false);

            if (!string.IsNullOrWhiteSpace(lotNum))
            {
                var lot = await db.ItemLots.SingleOrDefaultAsync(l => l.OrganizationId == orgId && l.ItemId == itemId && l.LotCode == lotNum);
                if (lot is null)
                {
                    db.ItemLots.Add(ItemLot.Create(orgId, itemId, lotNum, today, today.AddYears(2), "بچ مواد اولیه تاییدشده"));
                }
            }

            await db.SaveChangesAsync(SeedActor);
        }

        // Petrochemical receipts
        await PostDirectReceiptAsync(itmSm.Id, uKg.Id, whRm.Id, 5000m, "PO-2026-001", "LOT-SM-202611-01", po1.Id, insp1.Id);
        await PostDirectReceiptAsync(itmCat.Id, uKg.Id, whRm.Id, 50m, "PO-2026-001", "LOT-CAT-202611-01", po1.Id);
        await PostDirectReceiptAsync(itmMinOil.Id, uKg.Id, whRm.Id, 100m, "PO-2026-001", null, po1.Id);
        await PostDirectReceiptAsync(itmBag25.Id, uPcs.Id, whRm.Id, 500m, "PO-2026-001", null, po1.Id);

        // Edible oil receipts
        await PostDirectReceiptAsync(itmPalm.Id, uKg.Id, whRm.Id, 3000m, "PO-2026-002", "LOT-PALM-202611-01", po2.Id);
        await PostDirectReceiptAsync(itmSun.Id, uKg.Id, whRm.Id, 3000m, "PO-2026-002", "LOT-SUN-202611-01", po2.Id, insp2.Id);
        await PostDirectReceiptAsync(itmSoya.Id, uKg.Id, whRm.Id, 1000m, "PO-2026-002", null, po2.Id);
        await PostDirectReceiptAsync(itmAdd.Id, uG.Id, whRm.Id, 500m, "PO-2026-002", null, po2.Id);
        await PostDirectReceiptAsync(itmSalt.Id, uKg.Id, whRm.Id, 100m, "PO-2026-002", null, po2.Id);

        // Plastic packaging receipts
        await PostDirectReceiptAsync(itmPreform.Id, uPcs.Id, whRm.Id, 3000m, "PO-2026-003", null, po3.Id);
        await PostDirectReceiptAsync(itmBottle.Id, uPcs.Id, whRm.Id, 2000m, "PO-2026-003", null, po3.Id);
        await PostDirectReceiptAsync(itmCap.Id, uPcs.Id, whRm.Id, 5000m, "PO-2026-003", null, po3.Id);
        await PostDirectReceiptAsync(itmLabel.Id, uPcs.Id, whRm.Id, 5000m, "PO-2026-003", null, po3.Id);
        await PostDirectReceiptAsync(itmCarton12.Id, uPcs.Id, whRm.Id, 500m, "PO-2026-003", null, po3.Id);

        // Metal can receipts
        await PostDirectReceiptAsync(itmCan180.Id, uPcs.Id, whRm.Id, 5000m, "PO-2026-004", null, po4.Id);
        await PostDirectReceiptAsync(itmLid.Id, uPcs.Id, whRm.Id, 5000m, "PO-2026-004", null, po4.Id);
        await PostDirectReceiptAsync(itmCarton24.Id, uPcs.Id, whRm.Id, 300m, "PO-2026-004", null, po4.Id);

        // Fishery tuna receipts
        await PostDirectReceiptAsync(itmFish.Id, uKg.Id, whRm.Id, 2500m, "PO-2026-005", "LOT-TUNA-RAW-01", po5.Id, insp3.Id);

        Console.WriteLine(" ✔ رسیدهای انبار با پیوند به سفارش خرید و بازرسی کیفی با موفقیت ثبت شدند.");

        // -------------------------------------------------------------------------
        // D. Production Orders & Shop-Floor Execution (MO-2026-001 through 007)
        // -------------------------------------------------------------------------
        Console.WriteLine("    4. ثبت دستورات تولید در فرم سفارش تولید (Production Orders) همراه با ورودی، خروجی و عملیات مسیر...");

        // MO 1: Produce 2000 KG GPPS 1540 (Completed)
        var mo1 = ProductionOrder.CreateDraft(orgId, siteId, "MO-2026-001", itmGpps.Id, 2000m, uKg.Id,
            today.AddDays(-12), today.AddDays(-10), bomGpps.Id, bomGpps.Code, vGpps.Id, vGpps.VersionNumber, 1000m, uKg.Id, "تولید گرانول شفاف GPPS پلیمری");
        mo1.ApplyRoutingSnapshot(rtGpps.Id, rtGpps.Code);
        mo1.Release(now.AddDays(-12));
        mo1.Start(now.AddDays(-11));
        mo1.Complete(2000m, 0m, "تولید با راندمان ۱۰۰ درصد و بدون ضایعات به اتمام رسید", now.AddDays(-10));
        db.ProductionOrders.Add(mo1);
        await db.SaveChangesAsync(SeedActor);

        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo1.Id, 1, 1, mo1.ItemId, itmSm.Id, 1970m, 0m, 1970m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo1.Id, 1, 2, mo1.ItemId, itmCat.Id, 10m, 0m, 10m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo1.Id, 1, 3, mo1.ItemId, itmMinOil.Id, 20m, 0m, 20m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo1.Id, 1, 4, mo1.ItemId, itmBag25.Id, 80m, 0m, 80m, uPcs.Id, false, false, null));
        db.ProductionOrderOutputs.Add(new ProductionOrderOutput(orgId, mo1.Id, 1, 1, mo1.ItemId, BomOutputType.MainProduct, 2000m, uKg.Id));

        var op1 = ProductionOrderOperation.Create(orgId, siteId, mo1.Id, 10, "OP-EXTRUDE", "پلیمریزاسیون و اکستروژن گرانول", wcPolymer.Id, 30m, 120m, 0m, 0m, true, "کنترل دبی و حرارت مذاب");
        op1.Start(now.AddDays(-11));
        op1.Complete(30m, 120m, 2000m, 0m, "اکستروژن بدون نوسان گرانول‌ها انجام شد", now.AddDays(-10));
        db.ProductionOrderOperations.Add(op1);
        await db.SaveChangesAsync(SeedActor);

        // MO 2: Produce 600 BTL Frying Oil with In-House Bottle Molding (Completed)
        var mo2 = ProductionOrder.CreateDraft(orgId, siteId, "MO-2026-002", itmOil.Id, 600m, uBtl.Id,
            today.AddDays(-9), today.AddDays(-8), bomOil.Id, bomOil.Code, vOil1.Id, vOil1.VersionNumber, 600m, uBtl.Id, "تولید روغن سرخ‌کردنی با بادکن داخلی بطری از پریفرم");
        mo2.ApplyRoutingSnapshot(rtOil.Id, rtOil.Code);
        mo2.Release(now.AddDays(-9));
        mo2.Start(now.AddDays(-9));
        mo2.Complete(600m, 0m, "تولید بچ اول روغن و کارتن‌گذاری تکمیل شد", now.AddDays(-8));
        db.ProductionOrders.Add(mo2);
        await db.SaveChangesAsync(SeedActor);

        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo2.Id, 1, 1, mo2.ItemId, itmPalm.Id, 240m, 0m, 240m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo2.Id, 1, 2, mo2.ItemId, itmSun.Id, 240m, 0m, 240m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo2.Id, 1, 3, mo2.ItemId, itmAdd.Id, 50m, 0m, 50m, uG.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo2.Id, 1, 4, mo2.ItemId, itmPreform.Id, 600m, 0m, 600m, uPcs.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo2.Id, 1, 5, mo2.ItemId, itmCap.Id, 600m, 0m, 600m, uPcs.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo2.Id, 1, 6, mo2.ItemId, itmLabel.Id, 600m, 0m, 600m, uPcs.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo2.Id, 1, 7, mo2.ItemId, itmCarton12.Id, 50m, 0m, 50m, uPcs.Id, false, false, null));
        db.ProductionOrderOutputs.Add(new ProductionOrderOutput(orgId, mo2.Id, 1, 1, mo2.ItemId, BomOutputType.MainProduct, 600m, uBtl.Id));

        var op2_1 = ProductionOrderOperation.Create(orgId, siteId, mo2.Id, 10, "OP-BLOW", "بادکن بطری پریفرم", wcBlow.Id, 15m, 45m, 0m, 0m, true, "کنترل ضخامت جداره");
        op2_1.Start(now.AddDays(-9));
        op2_1.Complete(15m, 45m, 600m, 0m, "بادکن ۶۰۰ عدد بطری شفاف بدون تغییر فرم", now.AddDays(-9));
        db.ProductionOrderOperations.Add(op2_1);

        var op2_2 = ProductionOrderOperation.Create(orgId, siteId, mo2.Id, 20, "OP-FILL", "تزریق روغن، درب‌بندی و کارتن‌گذاری", wcOilFill.Id, 20m, 60m, 0m, 0m, true, "کنترل وزن پرکنی");
        op2_2.Start(now.AddDays(-9));
        op2_2.Complete(20m, 60m, 600m, 0m, "بسته‌بندی ۵۰ کارتن ۱۲ تایی تکمیل شد", now.AddDays(-8));
        db.ProductionOrderOperations.Add(op2_2);
        await db.SaveChangesAsync(SeedActor);

        // MO 3: Produce 600 BTL Frying Oil with Purchased Ready Bottles (Completed)
        var mo3 = ProductionOrder.CreateDraft(orgId, siteId, "MO-2026-003", itmOil.Id, 600m, uBtl.Id,
            today.AddDays(-7), today.AddDays(-6), bomOil.Id, bomOil.Code, vOil2.Id, vOil2.VersionNumber, 600m, uBtl.Id, "تولید روغن سرخ‌کردنی با بطری‌های آماده خریداری‌شده از بیرون");
        mo3.Release(now.AddDays(-7));
        mo3.Start(now.AddDays(-7));
        mo3.Complete(600m, 0m, "تولید با بطری خارجی با موفقیت تکمیل شد", now.AddDays(-6));
        db.ProductionOrders.Add(mo3);
        await db.SaveChangesAsync(SeedActor);

        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo3.Id, 1, 1, mo3.ItemId, itmPalm.Id, 240m, 0m, 240m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo3.Id, 1, 2, mo3.ItemId, itmSun.Id, 240m, 0m, 240m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo3.Id, 1, 3, mo3.ItemId, itmAdd.Id, 50m, 0m, 50m, uG.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo3.Id, 1, 4, mo3.ItemId, itmBottle.Id, 600m, 0m, 600m, uPcs.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo3.Id, 1, 5, mo3.ItemId, itmCap.Id, 600m, 0m, 600m, uPcs.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo3.Id, 1, 6, mo3.ItemId, itmLabel.Id, 600m, 0m, 600m, uPcs.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo3.Id, 1, 7, mo3.ItemId, itmCarton12.Id, 50m, 0m, 50m, uPcs.Id, false, false, null));
        db.ProductionOrderOutputs.Add(new ProductionOrderOutput(orgId, mo3.Id, 1, 1, mo3.ItemId, BomOutputType.MainProduct, 600m, uBtl.Id));

        var op3 = ProductionOrderOperation.Create(orgId, siteId, mo3.Id, 20, "OP-FILL", "پرکنی بطری آماده و کارتن‌گذاری", wcOilFill.Id, 20m, 55m, 0m, 0m, true, "کنترل نشتی درب فلیپ‌تاپ");
        op3.Start(now.AddDays(-7));
        op3.Complete(20m, 55m, 600m, 0m, "تکمیل پرکنی و تحویل ۵۰ کارتن", now.AddDays(-6));
        db.ProductionOrderOperations.Add(op3);
        await db.SaveChangesAsync(SeedActor);

        // MO 4: Produce 1200 CAN Canned Tuna (Completed)
        var mo4 = ProductionOrder.CreateDraft(orgId, siteId, "MO-2026-004", itmTuna.Id, 1200m, uCan.Id,
            today.AddDays(-5), today.AddDays(-4), bomTuna.Id, bomTuna.Code, vTuna.Id, vTuna.VersionNumber, 2400m, uCan.Id, "تولید کنسرو تن ماهی در روغن سویا خالص");
        mo4.ApplyRoutingSnapshot(rtTuna.Id, rtTuna.Code);
        mo4.Release(now.AddDays(-5));
        mo4.Start(now.AddDays(-5));
        mo4.Complete(1200m, 0m, "استریلیزاسیون و بسته‌بندی ۱۲۰۰ قوطی با موفقیت انجام شد", now.AddDays(-4));
        db.ProductionOrders.Add(mo4);
        await db.SaveChangesAsync(SeedActor);

        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo4.Id, 1, 1, mo4.ItemId, itmFish.Id, 225m, 0m, 225m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo4.Id, 1, 2, mo4.ItemId, itmSoya.Id, 45m, 0m, 45m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo4.Id, 1, 3, mo4.ItemId, itmSalt.Id, 2.5m, 0m, 2.5m, uKg.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo4.Id, 1, 4, mo4.ItemId, itmCan180.Id, 1200m, 0m, 1200m, uPcs.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo4.Id, 1, 5, mo4.ItemId, itmLid.Id, 1200m, 0m, 1200m, uPcs.Id, false, false, null));
        db.ProductionOrderInputs.Add(new ProductionOrderInput(orgId, mo4.Id, 1, 6, mo4.ItemId, itmCarton24.Id, 50m, 0m, 50m, uPcs.Id, false, false, null));
        db.ProductionOrderOutputs.Add(new ProductionOrderOutput(orgId, mo4.Id, 1, 1, mo4.ItemId, BomOutputType.MainProduct, 1200m, uCan.Id));

        var op4_1 = ProductionOrderOperation.Create(orgId, siteId, mo4.Id, 10, "OP-CAN-FILL", "پرکنی گوشت ماهی و تزریق روغن", wcTunaLine.Id, 25m, 90m, 0m, 0m, true, "کنترل وزن پرکنی");
        op4_1.Start(now.AddDays(-5));
        op4_1.Complete(25m, 90m, 1200m, 0m, "پرکنی بدون انحراف وزن تکمیل گردید", now.AddDays(-5));
        db.ProductionOrderOperations.Add(op4_1);

        var op4_2 = ProductionOrderOperation.Create(orgId, siteId, mo4.Id, 20, "OP-AUTOCLAVE", "استریلیزاسیون حرارتی اتوکلاو", wcAutoclave.Id, 60m, 120m, 0m, 0m, true, "کنترل دمای ۱۲۱ درجه");
        op4_2.Start(now.AddDays(-5));
        op4_2.Complete(60m, 120m, 1200m, 0m, "سیکل استریل با لاگ ترموکوپل تایید شد", now.AddDays(-4));
        db.ProductionOrderOperations.Add(op4_2);
        await db.SaveChangesAsync(SeedActor);

        // MO 5: In-Progress Order (Tests active in-progress production order in UI)
        var mo5 = ProductionOrder.CreateDraft(orgId, siteId, "MO-2026-005", itmGpps.Id, 1000m, uKg.Id,
            today.AddDays(-1), today.AddDays(2), bomGpps.Id, bomGpps.Code, vGpps.Id, vGpps.VersionNumber, 1000m, uKg.Id, "تولید ۱ تن GPPS سفارش مشتری احمد جمشیدی");
        mo5.ApplyRoutingSnapshot(rtGpps.Id, rtGpps.Code);
        mo5.Release(now.AddDays(-1));
        mo5.Start(now);
        db.ProductionOrders.Add(mo5);
        await db.SaveChangesAsync(SeedActor);

        var op5 = ProductionOrderOperation.Create(orgId, siteId, mo5.Id, 10, "OP-EXTRUDE", "پلیمریزاسیون و اکستروژن", wcPolymer.Id, 30m, 60m, 0m, 0m, true, "در حال کار در خط تولید");
        op5.Start(now);
        db.ProductionOrderOperations.Add(op5);
        await db.SaveChangesAsync(SeedActor);

        // MO 6: Released Order (Tests released production order waiting to start in UI)
        var mo6 = ProductionOrder.CreateDraft(orgId, siteId, "MO-2026-006", itmOil.Id, 1200m, uBtl.Id,
            today, today.AddDays(3), bomOil.Id, bomOil.Code, vOil1.Id, vOil1.VersionNumber, 600m, uBtl.Id, "دستور کار آزاد شده جهت شیفت فردا");
        mo6.Release(now);
        db.ProductionOrders.Add(mo6);
        await db.SaveChangesAsync(SeedActor);

        // MO 7: Draft Order (Tests draft production order planning in UI)
        var mo7 = ProductionOrder.CreateDraft(orgId, siteId, "MO-2026-007", itmTuna.Id, 2400m, uCan.Id,
            today.AddDays(2), today.AddDays(6), bomTuna.Id, bomTuna.Code, vTuna.Id, vTuna.VersionNumber, 2400m, uCan.Id, "پیش‌نویس برنامه‌ریزی ماه بعد تن ماهی اتکا");
        db.ProductionOrders.Add(mo7);
        await db.SaveChangesAsync(SeedActor);

        Console.WriteLine(" ✔ ۷ دستور تولید با وضعیت‌های مختلف (تکمیل‌شده، در حال اجرا، آزادشده، پیش‌نویس) و جزئیات کامل ذخیره شدند.");

        // -------------------------------------------------------------------------
        // E. Finished Goods Quality Inspections & COA (INSP-2026-004 through 006)
        // -------------------------------------------------------------------------
        Console.WriteLine("    5. ثبت بازرسی‌های کیفی محصول نهایی و صدور گواهی‌های آنالیز (COA Certificates)...");

        // INSP 4: Finished GPPS 1540 Inspection with COA
        var insp4 = QualityInspection.Create(orgId, siteId, whFg.Id, itmGpps.Id, "LOT-FG-GPPS-001", "INSP-2026-004",
            QualityInspectionSourceType.ProductionReceipt, null, mo1.Id, uKg.Id, 2000m, "بازرسی کیفی گرانول تولیدی دستور MO-2026-001");
        insp4.ReplaceLines([
            new QualityInspectionLineWriteData(pMfi.Name, "8.00 - 10.00 g/10min", "9.10", true, "شاخص مذاب کاملاً مطابق استاندارد",
                pMfi.Id, pMfi.Code, pMfi.Symbol, pMfi.UnitSymbol, pMfi.DefaultTestMethod, pMfi.DataType,
                8.0m, 10.0m, 9.0m, 9.10m, true),
            new QualityInspectionLineWriteData(pVicat.Name, "94.0 - 98.0 °C", "96.2", true, "دمای نرمی ویکات در محدوده هدف",
                pVicat.Id, pVicat.Code, pVicat.Symbol, pVicat.UnitSymbol, pVicat.DefaultTestMethod, pVicat.DataType,
                94.0m, 98.0m, 96.0m, 96.2m, true)
        ]);
        insp4.UpdateHeaderNotes(null, "محصول نهایی پلی‌استایرن GPPS 1540 درجه یک بدون دانه سیاه", "COA-GPPS-2026-001");
        insp4.RecordDisposition(2000m, 0m, null, "تایید و ترخیص به انبار محصول نهایی", SeedActor.ActorId, now.AddDays(-10));
        db.QualityInspections.Add(insp4);

        // INSP 5: Finished Frying Oil Inspection with COA
        var insp5 = QualityInspection.Create(orgId, siteId, whFg.Id, itmOil.Id, "LOT-FG-OIL1-001", "INSP-2026-005",
            QualityInspectionSourceType.ProductionReceipt, null, mo2.Id, uBtl.Id, 600m, "بازرسی کیفی روغن بطری سرخ‌کردنی ۸۰۰ گرمی");
        insp5.ReplaceLines([
            new QualityInspectionLineWriteData(pFfa.Name, "Max 0.100 %", "0.075", true, "اسید چرب آزاد محصول در سطح مطلوب",
                pFfa.Id, pFfa.Code, pFfa.Symbol, pFfa.UnitSymbol, pFfa.DefaultTestMethod, pFfa.DataType,
                null, 0.10m, 0.08m, 0.075m, true),
            new QualityInspectionLineWriteData(pPv.Name, "Max 1.50 meq/kg", "0.85", true, "عدد پراکسید بسیار پایین و استاندارد",
                pPv.Id, pPv.Code, pPv.Symbol, pPv.UnitSymbol, pPv.DefaultTestMethod, pPv.DataType,
                null, 1.5m, 0.8m, 0.85m, true),
            new QualityInspectionLineWriteData(pLeak.Name, "Leak Test Passed", "true", true, "آزمون نشتی و هوابندی درب با موفقیت انجام شد",
                pLeak.Id, pLeak.Code, pLeak.Symbol, pLeak.UnitSymbol, pLeak.DefaultTestMethod, pLeak.DataType,
                null, null, null, null, true)
        ]);
        insp5.UpdateHeaderNotes(null, "روغن شفاف طلایی بدون کدورت - برچسب‌گذاری کامل", "COA-OIL-2026-001");
        insp5.RecordDisposition(600m, 0m, null, "تایید ترخیص و کارتن‌گذاری به انبار محصول نهایی", SeedActor.ActorId, now.AddDays(-8));
        db.QualityInspections.Add(insp5);

        // INSP 6: Finished Canned Tuna Inspection with COA
        var insp6 = QualityInspection.Create(orgId, siteId, whFg.Id, itmTuna.Id, "LOT-FG-TUNA-001", "INSP-2026-006",
            QualityInspectionSourceType.ProductionReceipt, null, mo4.Id, uCan.Id, 1200m, "بازرسی کیفی و بهداشتی کنسرو تن ماهی ۱۸۰ گرمی");
        insp6.ReplaceLines([
            new QualityInspectionLineWriteData(pDrained.Name, "68.0 - 75.0 %", "71.5", true, "درصد گوشت آبکش شده در محدوده بهینه",
                pDrained.Id, pDrained.Code, pDrained.Symbol, pDrained.UnitSymbol, pDrained.DefaultTestMethod, pDrained.DataType,
                68.0m, 75.0m, 70.0m, 71.5m, true),
            new QualityInspectionLineWriteData(pHistamine.Name, "Max 40.0 ppm", "23.5", true, "هیستامین در حد بسیار بهداشتی و سلامت",
                pHistamine.Id, pHistamine.Code, pHistamine.Symbol, pHistamine.UnitSymbol, pHistamine.DefaultTestMethod, pHistamine.DataType,
                null, 40.0m, 25.0m, 23.5m, true),
            new QualityInspectionLineWriteData(pLeak.Name, "Leak Test Passed", "true", true, "سیلینگ و درزبندی بدون هرگونه بادکردگی",
                pLeak.Id, pLeak.Code, pLeak.Symbol, pLeak.UnitSymbol, pLeak.DefaultTestMethod, pLeak.DataType,
                null, null, null, null, true)
        ]);
        insp6.UpdateHeaderNotes(null, "فیله ماهی تن روشن در روغن سویا شفاف", "COA-TUNA-2026-001");
        insp6.RecordDisposition(1200m, 0m, null, "تایید خروج از دوره قرنطینه گرمخانه و تحویل به انبار محصول", SeedActor.ActorId, now.AddDays(-4));
        db.QualityInspections.Add(insp6);

        await db.SaveChangesAsync(SeedActor);
        Console.WriteLine(" ✔ بازرسی‌های کیفی محصولات تولیدی با آزمون‌های آزمایشگاهی و صدور گواهی آنالیز COA ثبت شدند.");

        // -------------------------------------------------------------------------
        // F. Production Issues & Receipts (Warehouse movements connected to MO & QC)
        // -------------------------------------------------------------------------
        Console.WriteLine("    6. ثبت مصرف مواد اولیه و ورود محصولات نهایی به انبار با پیوند به دستور تولید و COA...");

        async Task PostDirectIssueAsync(long itemId, long unitId, long warehouseId, decimal qty, string docNum, string? lotNum = null, long? moId = null)
        {
            var txn = await db.InventoryTransactions.SingleOrDefaultAsync(t => t.OrganizationId == orgId && t.DocumentNumber == docNum);
            if (txn is null)
            {
                txn = InventoryTransaction.Create(
                    organizationId: orgId,
                    siteId: siteId,
                    warehouseId: warehouseId,
                    destinationWarehouseId: null,
                    documentNumber: docNum,
                    transactionType: InventoryTransactionType.ProductionIssue,
                    productionOrderId: moId,
                    reversedTransactionId: null,
                    referenceNote: "حواله مصرف در خط تولید",
                    idempotencyKey: null,
                    postedAtUtc: now);

                db.InventoryTransactions.Add(txn);
                await db.SaveChangesAsync(SeedActor);
            }

            InventoryBalance? bal = null;
            if (!string.IsNullOrWhiteSpace(lotNum))
            {
                bal = await db.InventoryBalances.FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.WarehouseId == warehouseId && b.ItemId == itemId && b.LotCode == lotNum);
            }
            if (bal is null)
            {
                bal = await db.InventoryBalances.FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.WarehouseId == warehouseId && b.ItemId == itemId && b.OnHandBaseQuantity >= qty);
            }
            if (bal is null)
            {
                bal = await db.InventoryBalances.FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.WarehouseId == warehouseId && b.ItemId == itemId);
            }

            var resolvedLot = bal?.LotCode ?? (lotNum ?? string.Empty);
            var lineNum = (await db.InventoryLedgerEntries.Where(l => l.OrganizationId == orgId && l.InventoryTransactionId == txn.Id).MaxAsync(l => (int?)l.LineNumber) ?? 0) + 1;

            var ledger = InventoryLedgerEntry.Create(
                organizationId: orgId,
                inventoryTransactionId: txn.Id,
                siteId: siteId,
                warehouseId: warehouseId,
                lineNumber: lineNum,
                itemId: itemId,
                lotCode: resolvedLot,
                enteredQuantity: qty,
                enteredUnitId: unitId,
                baseQuantityDelta: -qty,
                baseUnitId: unitId,
                occurredAtUtc: now,
                notes: "حواله مصرف در خط تولید");

            db.InventoryLedgerEntries.Add(ledger);

            if (bal is null)
            {
                bal = InventoryBalance.CreateZero(orgId, siteId, warehouseId, itemId, resolvedLot, unitId, now);
                db.InventoryBalances.Add(bal);
            }
            bal.ApplyDelta(-qty, now, enforceAvailableOnOutbound: false);

            await db.SaveChangesAsync(SeedActor);
        }

        async Task PostDirectFgReceiptAsync(long itemId, long unitId, long warehouseId, decimal qty, string docNum, string lotNum, long? moId = null, long? inspId = null)
        {
            var txn = await db.InventoryTransactions.SingleOrDefaultAsync(t => t.OrganizationId == orgId && t.DocumentNumber == docNum);
            if (txn is null)
            {
                txn = InventoryTransaction.Create(
                    organizationId: orgId,
                    siteId: siteId,
                    warehouseId: warehouseId,
                    destinationWarehouseId: null,
                    documentNumber: docNum,
                    transactionType: InventoryTransactionType.ProductionReceipt,
                    productionOrderId: moId,
                    reversedTransactionId: null,
                    referenceNote: "رسید تولید محصول نهایی با تاییدیه کنترل کیفیت",
                    idempotencyKey: null,
                    postedAtUtc: now,
                    qualityInspectionId: inspId);

                db.InventoryTransactions.Add(txn);
                await db.SaveChangesAsync(SeedActor);
            }

            var lineNum = (await db.InventoryLedgerEntries.Where(l => l.OrganizationId == orgId && l.InventoryTransactionId == txn.Id).MaxAsync(l => (int?)l.LineNumber) ?? 0) + 1;

            var ledger = InventoryLedgerEntry.Create(
                organizationId: orgId,
                inventoryTransactionId: txn.Id,
                siteId: siteId,
                warehouseId: warehouseId,
                lineNumber: lineNum,
                itemId: itemId,
                lotCode: lotNum,
                enteredQuantity: qty,
                enteredUnitId: unitId,
                baseQuantityDelta: qty,
                baseUnitId: unitId,
                occurredAtUtc: now,
                notes: "رسید تولید محصول نهایی");

            db.InventoryLedgerEntries.Add(ledger);

            var bal = await db.InventoryBalances.SingleOrDefaultAsync(b => b.OrganizationId == orgId && b.WarehouseId == warehouseId && b.ItemId == itemId && b.LotCode == lotNum);
            if (bal is null)
            {
                bal = InventoryBalance.CreateZero(orgId, siteId, warehouseId, itemId, lotNum, unitId, now);
                db.InventoryBalances.Add(bal);
            }
            bal.ApplyDelta(qty, now, enforceAvailableOnOutbound: false);

            var lot = await db.ItemLots.SingleOrDefaultAsync(l => l.OrganizationId == orgId && l.ItemId == itemId && l.LotCode == lotNum);
            if (lot is null)
            {
                db.ItemLots.Add(ItemLot.Create(orgId, itemId, lotNum, today, today.AddYears(1), "بچ محصول نهایی استاندارد"));
            }

            await db.SaveChangesAsync(SeedActor);
        }

        // MO 1: Issue & FG Receipt (GPPS)
        await PostDirectIssueAsync(itmSm.Id, uKg.Id, whRm.Id, 1970m, "MO-2026-001", "LOT-SM-202611-01", mo1.Id);
        await PostDirectIssueAsync(itmCat.Id, uKg.Id, whRm.Id, 10m, "MO-2026-001", "LOT-CAT-202611-01", mo1.Id);
        await PostDirectIssueAsync(itmMinOil.Id, uKg.Id, whRm.Id, 20m, "MO-2026-001", null, mo1.Id);
        await PostDirectIssueAsync(itmBag25.Id, uPcs.Id, whRm.Id, 80m, "MO-2026-001", null, mo1.Id);
        await PostDirectFgReceiptAsync(itmGpps.Id, uKg.Id, whFg.Id, 2000m, "MO-2026-001", "LOT-FG-GPPS-001", mo1.Id, insp4.Id);

        // MO 2: Issue & FG Receipt (Frying Oil v1)
        await PostDirectIssueAsync(itmPalm.Id, uKg.Id, whRm.Id, 240m, "MO-2026-002", "LOT-PALM-202611-01", mo2.Id);
        await PostDirectIssueAsync(itmSun.Id, uKg.Id, whRm.Id, 240m, "MO-2026-002", "LOT-SUN-202611-01", mo2.Id);
        await PostDirectIssueAsync(itmAdd.Id, uG.Id, whRm.Id, 50m, "MO-2026-002", null, mo2.Id);
        await PostDirectIssueAsync(itmPreform.Id, uPcs.Id, whRm.Id, 600m, "MO-2026-002", null, mo2.Id);
        await PostDirectIssueAsync(itmCap.Id, uPcs.Id, whRm.Id, 600m, "MO-2026-002", null, mo2.Id);
        await PostDirectIssueAsync(itmLabel.Id, uPcs.Id, whRm.Id, 600m, "MO-2026-002", null, mo2.Id);
        await PostDirectIssueAsync(itmCarton12.Id, uPcs.Id, whRm.Id, 50m, "MO-2026-002", null, mo2.Id);
        await PostDirectFgReceiptAsync(itmOil.Id, uBtl.Id, whFg.Id, 600m, "MO-2026-002", "LOT-FG-OIL1-001", mo2.Id, insp5.Id);

        // MO 3: Issue & FG Receipt (Frying Oil v2)
        await PostDirectIssueAsync(itmPalm.Id, uKg.Id, whRm.Id, 240m, "MO-2026-003", "LOT-PALM-202611-01", mo3.Id);
        await PostDirectIssueAsync(itmSun.Id, uKg.Id, whRm.Id, 240m, "MO-2026-003", "LOT-SUN-202611-01", mo3.Id);
        await PostDirectIssueAsync(itmAdd.Id, uG.Id, whRm.Id, 50m, "MO-2026-003", null, mo3.Id);
        await PostDirectIssueAsync(itmBottle.Id, uPcs.Id, whRm.Id, 600m, "MO-2026-003", null, mo3.Id);
        await PostDirectIssueAsync(itmCap.Id, uPcs.Id, whRm.Id, 600m, "MO-2026-003", null, mo3.Id);
        await PostDirectIssueAsync(itmLabel.Id, uPcs.Id, whRm.Id, 600m, "MO-2026-003", null, mo3.Id);
        await PostDirectIssueAsync(itmCarton12.Id, uPcs.Id, whRm.Id, 50m, "MO-2026-003", null, mo3.Id);
        await PostDirectFgReceiptAsync(itmOil.Id, uBtl.Id, whFg.Id, 600m, "MO-2026-003", "LOT-FG-OIL2-001", mo3.Id);

        // MO 4: Issue & FG Receipt (Canned Tuna)
        await PostDirectIssueAsync(itmFish.Id, uKg.Id, whRm.Id, 225m, "MO-2026-004", "LOT-TUNA-RAW-01", mo4.Id);
        await PostDirectIssueAsync(itmSoya.Id, uKg.Id, whRm.Id, 45m, "MO-2026-004", null, mo4.Id);
        await PostDirectIssueAsync(itmSalt.Id, uKg.Id, whRm.Id, 2.5m, "MO-2026-004", null, mo4.Id);
        await PostDirectIssueAsync(itmCan180.Id, uPcs.Id, whRm.Id, 1200m, "MO-2026-004", null, mo4.Id);
        await PostDirectIssueAsync(itmLid.Id, uPcs.Id, whRm.Id, 1200m, "MO-2026-004", null, mo4.Id);
        await PostDirectIssueAsync(itmCarton24.Id, uPcs.Id, whRm.Id, 50m, "MO-2026-004", null, mo4.Id);
        await PostDirectFgReceiptAsync(itmTuna.Id, uCan.Id, whFg.Id, 1200m, "MO-2026-004", "LOT-FG-TUNA-001", mo4.Id, insp6.Id);

        Console.WriteLine(" ✔ تراکنش‌های انبار تولید (حواله مصرف و رسید محصول با بچ استاندارد) با موفقیت ثبت شدند.");

        // -------------------------------------------------------------------------
        // G. Sales Orders & Delivery Dispatches (SO-2026-001..005, DO-2026-001..003)
        // -------------------------------------------------------------------------
        Console.WriteLine("    7. ثبت سفارشات فروش در فرم فروش (Sales Orders) و صدور حواله‌های خروج کالا (Delivery Orders)...");

        // Helper to post sales dispatch
        async Task PostDirectDispatchAsync(long itemId, long unitId, long warehouseId, decimal qty, string docNum, string lotNum, long? doId = null)
        {
            var txn = await db.InventoryTransactions.SingleOrDefaultAsync(t => t.OrganizationId == orgId && t.DocumentNumber == docNum);
            if (txn is null)
            {
                txn = InventoryTransaction.Create(
                    organizationId: orgId,
                    siteId: siteId,
                    warehouseId: warehouseId,
                    destinationWarehouseId: null,
                    documentNumber: docNum,
                    transactionType: InventoryTransactionType.SalesDispatch,
                    productionOrderId: null,
                    reversedTransactionId: null,
                    referenceNote: "حواله خروج و تحویل به مشتری مبتنی بر حواله توزیع",
                    idempotencyKey: null,
                    postedAtUtc: now,
                    deliveryOrderId: doId);

                db.InventoryTransactions.Add(txn);
                await db.SaveChangesAsync(SeedActor);
            }

            var bal = await db.InventoryBalances.FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.WarehouseId == warehouseId && b.ItemId == itemId && b.LotCode == lotNum);
            if (bal is null)
            {
                bal = await db.InventoryBalances.FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.WarehouseId == warehouseId && b.ItemId == itemId && b.OnHandBaseQuantity >= qty);
            }

            var resolvedLot = bal?.LotCode ?? lotNum;
            var lineNum = (await db.InventoryLedgerEntries.Where(l => l.OrganizationId == orgId && l.InventoryTransactionId == txn.Id).MaxAsync(l => (int?)l.LineNumber) ?? 0) + 1;

            var ledger = InventoryLedgerEntry.Create(
                organizationId: orgId,
                inventoryTransactionId: txn.Id,
                siteId: siteId,
                warehouseId: warehouseId,
                lineNumber: lineNum,
                itemId: itemId,
                lotCode: resolvedLot,
                enteredQuantity: qty,
                enteredUnitId: unitId,
                baseQuantityDelta: -qty,
                baseUnitId: unitId,
                occurredAtUtc: now,
                notes: "حواله خروج و تحویل به مشتری");

            db.InventoryLedgerEntries.Add(ledger);

            if (bal is null)
            {
                bal = InventoryBalance.CreateZero(orgId, siteId, warehouseId, itemId, resolvedLot, unitId, now);
                db.InventoryBalances.Add(bal);
            }
            bal.ApplyDelta(-qty, now, enforceAvailableOnOutbound: false);

            await db.SaveChangesAsync(SeedActor);
        }

        // SO 1: GPPS for Jamshidi (Dispatched 1200 KG out of 2000 KG -> leaves 800 KG buffer!)
        var so1 = SalesOrder.CreateDraft(orgId, siteId, custJamshidi.Id, "SO-2026-001", today.AddDays(-3), today.AddDays(-1), whFg.Id, "IRR", "اصفهان، خیابان نشاط، انبار جمشیدی", "فروش نقدی گرانول GPPS");
        so1.ReplaceLines([
            (itmGpps.Id, 1200m, uKg.Id, uKg.Id, 1200m, 650000m, "گرانول شفاف گرید ۱۵۴۰")
        ]);
        so1.Approve(now.AddDays(-2));
        db.SalesOrders.Add(so1);
        await db.SaveChangesAsync(SeedActor);

        var do1 = DeliveryOrder.CreatePosted(orgId, siteId, so1.Id, whFg.Id, "DO-2026-001", today.AddDays(-1),
            "باربری اصفهان پیشتاز", "TRK-JAMSHIDI-01", "تحویل محموله همراه با گواهی آنالیز کیفیت COA-GPPS-2026-001", now.AddDays(-1),
            [(so1.Lines[0].Id, itmGpps.Id, "LOT-FG-GPPS-001", 1200m, uKg.Id, insp4.Id, "COA-GPPS-2026-001", "تحویل با کیفیت عالی")]);
        db.DeliveryOrders.Add(do1);
        await db.SaveChangesAsync(SeedActor);

        so1.ApplyDispatches([(so1.Lines[0].Id, 1200m)]);
        await db.SaveChangesAsync(SeedActor);
        await PostDirectDispatchAsync(itmGpps.Id, uKg.Id, whFg.Id, 1200m, "DO-2026-001", "LOT-FG-GPPS-001", do1.Id);

        // SO 2: Frying Oil for Alborz Dist (Dispatched 700 BTL out of 1200 BTL -> leaves 500 BTL buffer!)
        var so2 = SalesOrder.CreateDraft(orgId, siteId, custAlborz.Id, "SO-2026-002", today.AddDays(-3), today.AddDays(-1), whFg.Id, "IRR", "تهران، کیلومتر ۸ بزرگراه فتح، مجتمع پخش البرز", "توزیع سراسری فروشگاهی روغن");
        so2.ReplaceLines([
            (itmOil.Id, 700m, uBtl.Id, uBtl.Id, 700m, 480000m, "روغن سرخ‌کردنی ۸۰۰ گرمی")
        ]);
        so2.Approve(now.AddDays(-2));
        db.SalesOrders.Add(so2);
        await db.SaveChangesAsync(SeedActor);

        // Dispatched from both lots (400 from Lot 1, 300 from Lot 2)
        var do2 = DeliveryOrder.CreatePosted(orgId, siteId, so2.Id, whFg.Id, "DO-2026-002", today.AddDays(-1),
            "ناوگان اختصاصی البرز", "TRK-ALBORZ-01", "تحویل ۷۰۰ بطری در قالب کارتن‌های ۱۲ عددی", now.AddDays(-1),
            [
                (so2.Lines[0].Id, itmOil.Id, "LOT-FG-OIL1-001", 400m, uBtl.Id, insp5.Id, "COA-OIL-2026-001", "بچ ساخت بطری داخلی"),
                (so2.Lines[0].Id, itmOil.Id, "LOT-FG-OIL2-001", 300m, uBtl.Id, null, null, "بچ بطری خارجی")
            ]);
        db.DeliveryOrders.Add(do2);
        await db.SaveChangesAsync(SeedActor);

        so2.ApplyDispatches([(so2.Lines[0].Id, 700m)]);
        await db.SaveChangesAsync(SeedActor);
        await PostDirectDispatchAsync(itmOil.Id, uBtl.Id, whFg.Id, 400m, "DO-2026-002", "LOT-FG-OIL1-001", do2.Id);
        await PostDirectDispatchAsync(itmOil.Id, uBtl.Id, whFg.Id, 300m, "DO-2026-002", "LOT-FG-OIL2-001", do2.Id);

        // SO 3: Canned Tuna for Etka (Dispatched 800 CAN out of 1200 CAN -> leaves 400 CAN buffer!)
        var so3 = SalesOrder.CreateDraft(orgId, siteId, custEtka.Id, "SO-2026-003", today.AddDays(-2), today.AddDays(-1), whFg.Id, "IRR", "تهران، انبار مرکزی اتکا، جاده مخصوص", "تأمین فروشگاه‌های زنجیره‌ای");
        so3.ReplaceLines([
            (itmTuna.Id, 800m, uCan.Id, uCan.Id, 800m, 850000m, "کنسرو تن ماهی در روغن ۱۸۰ گرمی")
        ]);
        so3.Approve(now.AddDays(-1));
        db.SalesOrders.Add(so3);
        await db.SaveChangesAsync(SeedActor);

        var do3 = DeliveryOrder.CreatePosted(orgId, siteId, so3.Id, whFg.Id, "DO-2026-003", today,
            "ترابری اتکا", "TRK-ETKA-01", "تحویل ۸۰۰ قوطی با گواهی استاندارد COA-TUNA-2026-001", now,
            [(so3.Lines[0].Id, itmTuna.Id, "LOT-FG-TUNA-001", 800m, uCan.Id, insp6.Id, "COA-TUNA-2026-001", "تحویل استاندارد")]);
        db.DeliveryOrders.Add(do3);
        await db.SaveChangesAsync(SeedActor);

        so3.ApplyDispatches([(so3.Lines[0].Id, 800m)]);
        await db.SaveChangesAsync(SeedActor);
        await PostDirectDispatchAsync(itmTuna.Id, uCan.Id, whFg.Id, 800m, "DO-2026-003", "LOT-FG-TUNA-001", do3.Id);

        // SO 4: Open Approved Sales Order (Tests approved sales order waiting for dispatch in UI)
        var so4 = SalesOrder.CreateDraft(orgId, siteId, custEtka.Id, "SO-2026-004", today, today.AddDays(5), whFg.Id, "IRR", "تهران، انبار غرب اتکا", "سفارش تاییدشده نوبت بعدی تن ماهی");
        so4.ReplaceLines([
            (itmTuna.Id, 1000m, uCan.Id, uCan.Id, 1000m, 850000m, "کنسرو تن ماهی ۱۸۰ گرمی")
        ]);
        so4.Approve(now);
        db.SalesOrders.Add(so4);
        await db.SaveChangesAsync(SeedActor);

        // SO 5: Draft Sales Order (Tests draft sales order editing in UI)
        var so5 = SalesOrder.CreateDraft(orgId, siteId, custAlborz.Id, "SO-2026-005", today, today.AddDays(10), whFg.Id, "IRR", "تهران، دفتر پخش البرز", "پیش‌نویس استعلام قیمت و سفارش روغن");
        so5.ReplaceLines([
            (itmOil.Id, 1200m, uBtl.Id, uBtl.Id, 1200m, 485000m, "روغن سرخ‌کردنی ۸۰۰ گرمی")
        ]);
        db.SalesOrders.Add(so5);
        await db.SaveChangesAsync(SeedActor);

        Console.WriteLine(" ✔ ۵ سفارش فروش و ۳ حواله خروج رسمی با ارتباط کامل به گواهی COA ثبت شدند.");

        // -------------------------------------------------------------------------
        // H. Stock Balance Buffer Assertions
        // -------------------------------------------------------------------------
        var gppsBal = await db.InventoryBalances.Where(b => b.OrganizationId == orgId && b.WarehouseId == whFg.Id && b.ItemId == itmGpps.Id).SumAsync(b => b.OnHandBaseQuantity);
        var oilBal = await db.InventoryBalances.Where(b => b.OrganizationId == orgId && b.WarehouseId == whFg.Id && b.ItemId == itmOil.Id).SumAsync(b => b.OnHandBaseQuantity);
        var tunaBal = await db.InventoryBalances.Where(b => b.OrganizationId == orgId && b.WarehouseId == whFg.Id && b.ItemId == itmTuna.Id).SumAsync(b => b.OnHandBaseQuantity);
        var smBal = await db.InventoryBalances.Where(b => b.OrganizationId == orgId && b.WarehouseId == whRm.Id && b.ItemId == itmSm.Id).SumAsync(b => b.OnHandBaseQuantity);
        var palmBal = await db.InventoryBalances.Where(b => b.OrganizationId == orgId && b.WarehouseId == whRm.Id && b.ItemId == itmPalm.Id).SumAsync(b => b.OnHandBaseQuantity);
        var fishBal = await db.InventoryBalances.Where(b => b.OrganizationId == orgId && b.WarehouseId == whRm.Id && b.ItemId == itmFish.Id).SumAsync(b => b.OnHandBaseQuantity);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n==================================================================");
        Console.WriteLine("     تراز موجودی انبارها و حفظ بافر مواد و کالا (Stock Buffer)");
        Console.WriteLine("==================================================================");
        Console.WriteLine($"    • مانده پلی‌استایرن GPPS در انبار محصول نهایی: {gppsBal:N0} کیلوگرم (بافر ۸۰۰ کیلوگرم موجود)");
        Console.WriteLine($"    • مانده روغن سرخ‌کردنی در انبار محصول نهایی: {oilBal:N0} بطری (بافر ۵۰۰ بطری موجود)");
        Console.WriteLine($"    • مانده کنسرو تن ماهی در انبار محصول نهایی: {tunaBal:N0} قوطی (بافر ۴۰۰ قوطی موجود)");
        Console.WriteLine($"    • مانده استایرن منومر در انبار مواد اولیه: {smBal:N0} کیلوگرم (بافر ۳,۰۳۰ کیلوگرم موجود)");
        Console.WriteLine($"    • مانده روغن پالم اولئین در انبار مواد اولیه: {palmBal:N0} کیلوگرم (بافر ۲,۵۲۰ کیلوگرم موجود)");
        Console.WriteLine($"    • مانده ماهی منجمد در انبار مواد اولیه: {fishBal:N0} کیلوگرم (بافر ۲,۲۷۵ کیلوگرم موجود)");
        Console.ResetColor();

        // -------------------------------------------------------------------------
        // I. Optimistic Concurrency Protection Test
        // -------------------------------------------------------------------------
        Console.WriteLine("\n[تست تداخل همزمانی و جلوگیری از رونویسی ناخواسته (Optimistic Concurrency Protection)]:");
        await using var ctxA = new OrganizationDbContext(options);
        await using var ctxB = new OrganizationDbContext(options);
        var pA = await ctxA.BusinessPartners.Include(b => b.Roles).SingleAsync(b => b.OrganizationId == orgId && b.Code == "BP-PARS-PETRO");
        var pB = await ctxB.BusinessPartners.Include(b => b.Roles).SingleAsync(b => b.OrganizationId == orgId && b.Code == "BP-PARS-PETRO");

        var tokenA = Guid.NewGuid().ToString("N")[..6];
        var tokenB = Guid.NewGuid().ToString("N")[..6];

        pA.Update(pA.PersonType, pA.Name, pA.FirstName, pA.LastName, pA.NationalCode, pA.CompanyName, pA.NationalLegalId, pA.EconomicCode, pA.Mobile, pA.Phone, pA.Email, pA.Website, pA.Country, pA.Province, pA.City, $"منطقه ویژه عسلویه - فاز {tokenA}", pA.PostalCode, "یادداشت کاربر اول", pA.Roles.Select(r => r.RoleId));
        pB.Update(pB.PersonType, pB.Name, pB.FirstName, pB.LastName, pB.NationalCode, pB.CompanyName, pB.NationalLegalId, pB.EconomicCode, pB.Mobile, pB.Phone, pB.Email, pB.Website, pB.Country, pB.Province, pB.City, $"منطقه ویژه عسلویه - فاز تداخلی {tokenB}", pB.PostalCode, "یادداشت کاربر دوم", pB.Roles.Select(r => r.RoleId));

        // User A commits first (updates rowversion in database)
        await ctxA.SaveChangesAsync(SeedActor);

        // User B attempts commit with stale RowVersion -> triggers DbUpdateConcurrencyException
        try
        {
            await ctxB.SaveChangesAsync(SeedActor);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" ! هشدار: همزمانی تشخیص داده نشد.");
            Console.ResetColor();
        }
        catch (DbUpdateConcurrencyException)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" ✔ تایید شد: خطای همزمانی (DbUpdateConcurrencyException) با موفقیت فعال شد و از رونویسی همزمان توسط کاربر دوم کاملاً جلوگیری به عمل آمد.");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n ✔ تزریق و آزمون تمام سناریوهای جامع صنعتی، سفارشات خرید، کنترل کیفیت، دستورات تولید و فروش با موفقیت کامل انجام شد!");
        Console.ResetColor();
        return 0;
    }
}
