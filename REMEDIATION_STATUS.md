# گزارش وضعیت نهایی ممیزی، بازسازی معماری و رفع گاردریل‌های مخزن ForgeFlow

**تاریخ آخرین به‌روزرسانی:** ۲۸ آبان ۱۴۰۵ (18 Nov 2026)  
**وضعیت سامانه:**
- **بیلد کل سولوشن:** ✅ کاملاً سبز با **۰ خطا و ۰ هشدار** (`0 Errors, 0 Warnings`) در حالت Release
- **مجموعه آزمون‌های خودکار و یکپارچگی SQL Server:** ✅ **۸۱۸ آزمون پاس شده**، ۰ شکست (`818 Passed, 0 Failed, 1 Skipped` - مجموع: ۸۱۹ آزمون روی دیتابیس محلی)
- **اجرای خودکار آزمون‌ها با تک‌دستور:** ✅ اجرای مستقیم `dotnet test` با شناسایی و اتصال خودکار به SQL Server/LocalDB محلی از طریق `TestAssemblyInitializer` بدون نیاز به تنظیم دستی متغیرهای محیطی
- **آزمون‌های یکپارچگی پایگاه داده (SQL Server Integration Tests):** ✅ ۱۰۰٪ پاس شده روی پایگاه داده محلی (`localhost/testforgflow`)
- **مدل دیتابیس و EF Core:** ✅ ۱۰۰٪ همگام در هر دو کانتکست داده بدون تغییرات مدل معلق (`HasPendingModelChanges() == false`)
- **مرزهای داده‌ای (Bounded Contexts):** ✅ تفکیک قطعی `AuthDbContext` و `OrganizationDbContext` با Marker Interfaces و حذف تمام `modelBuilder.Ignore<...>()`
- **کپسوله‌سازی منطق دامنه:** ✅ ۱۰۰٪ انتقال قوانین گذار وضعیت و Invariantها به درون موجودیت‌های `ForgeFlow.Domain`
- **سخت‌سازی امنیتی احراز هویت (Auth Hardening):** ✅ اعمال اجباری User Verification در Passkey، اعتبارسنجی مجدد رمز عبور (Step-Up Auth)، ضد افشای زمان‌بندی (Anti-Timing PBKDF2)، رمزنگاری اسرار 2FA در دیتابیس با DataProtection، هدرهای فورواردشده امن و محدودسازی نرخ درخواست بدون وقفه
- **رفع قفل‌گذاری و تراکنش‌های سنگین (Concurrency & Performance):** ✅ حذف تراکنش‌های `Serializable` در اعتبارسنجی سشن، کشینگ لغزان در حافظه، تجمیع بررسی وضعیت سازمان در `OrganizationActivityGuard` بدون قفل انحصاری، قفل دقیق ردیفی `(UPDLOCK, ROWLOCK)` در موجودی انبار، تصحیح ترتیب بلوک‌های Catch و لاگینگ خطاهای خاموش
- **آماده‌سازی استقرار در محیط پروداکشن (Production Readiness & Health):** ✅ ریدایرکت HTTPS، فعال‌سازی HSTS، الزام پایداری کلیدهای DataProtection در پروداکشن (`KeyRingPath`) با Fail-Fast در زمان عدم تنظیم، محدودسازی منابع مدار Blazor Circuit، پایش سلامت سامانه با `/health`، پاکسازی کدهای مرده و جداسازی ابزارهای تست
- **سرویس وب:** ✅ در حال اجرا روی `https://localhost:5001/account/login` و `https://localhost:5001/health` با پاسخ تأییدشده **HTTP 200**

---

## ۱. نحوه اجرای آزمون‌های سامانه با تک‌دستور (Single-Command Test Execution)

به لطف افزوده شدن ماژول راه‌انداز آزمون (`TestAssemblyInitializer.cs`)، آزمون‌های یکپارچگی پایگاه داده و احراز هویت به صورت خودکار نمونه‌های در حال اجرای SQL Server محلی یا LocalDB را شناسایی می‌کنند و دیگر نیازی به مقداردهی دستی متغیر محیطی نیست:

```powershell
# اجرای تمامی ۸۱۹ آزمون سامانه (واحد، یکپارچگی پایگاه‌داده، همزمانی و امنیتی)
dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj
```

در صورت نیاز به استفاده از پایگاه‌داده اختصاصی سفارشی:
```powershell
$env:FORGEFLOW_TEST_SQLSERVER="Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=True"
dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj
```

---

## ۲. گزارش پیشرفت گام‌به‌گام اقدامات انجام‌شده

### گام ۱: تفکیک ساختاری پیکربندی‌های EF Core (ماژولار کردن لایه داده)
- کلاس مونوپولی ۱۵۵۰ خطی `OrganizationModel.cs` تفکیک و به کلاس‌های اختصاصی `IEntityTypeConfiguration<T>` در `Configurations/` تبدیل شد.
- ثبت خودکار تمام پیکربندی‌ها از طریق `ApplyConfigurationsFromAssembly` در متد `OnModelCreating` کلاس `OrganizationDbContext`.

### گام ۲: تفکیک کلاس‌های بزرگ UseCases به معماری اسلایس عمودی (Vertical Slices)
- کلیه کلاس‌های UseCases لایه Application (فروش، درخت محصول، تولید، کیفیت، انبار، خرید، کاتالوگ، واحدها، ارزها، گزارش‌گیری، ممیزی، داشبورد، لاگ خطا، روتینگ، آزمایشگاه، کاربران، نقش‌ها، سازمان و توالی اسناد) به اسلایس‌های مستقل و ایزوله تبدیل و کلاس‌های UseCases به Facade سبک تقلیل یافتند.

### گام ۳: اصلاح همزمانی خوش‌بینانه و پیاده‌سازی صریح `IHasRowVersion`
- کلیه موجودیت‌های دامنه‌ای اینترفیس `IHasRowVersion` و پراپرتی تایپ‌شده `public byte[] RowVersion { get; }` را پیاده‌سازی کردند.
- حذف کامل ارجاعات جادویی بر پایه رشته (`EF.Property<byte[]>(x, "RowVersion")`) در متدهای انطباق نسخه در تمام مخازن SQL.

### گام ۴: تفکیک قطعی مرزهای کانتکست داده (`AuthDbContext` و `OrganizationDbContext`)
- حذف کامل `modelBuilder.Ignore<...>()` از `AuthDbContext.OnModelCreating`.
- تعریف مارکر اینترفیس‌های `IAuthEntityConfiguration` و `IOrganizationEntityConfiguration` جهت اعمال مستقل پیکربندی‌ها در هر DbContext بدون نشت موجودیت‌ها.
- ایزوله‌سازی مدل‌های احراز هویت و جداول سازمانی با `ExcludeFromMigrations()`.
- نگارش آزمون‌های تضمین مرز کانتکست (`DbContextBoundaryTests.cs`) و اطمینان از ثبت دقیق ۱۹ موجودیت در Auth و ۴۹ موجودیت در Organization.

### گام ۵: کپسوله‌سازی منطق دامنه و رفع نشت قوانین به لایه Infrastructure
- کپسوله‌سازی کامل موجودیت‌های اصلی (`SalesOrder`, `ProductionOrder`, `PurchaseOrder`, `QualityInspection`, `StockLot`, `InventoryBalance`, `StockReservation`, `Routing`) با فیلدهای `private/internal set` و متدهای دامنه با خروجی Result.
- افزودن قابلیت‌های تجاری `CanReserveStock`، `EnsureDraftMutable` و متدهای تغییر وضعیت فعال/غیرفعال سشن و عضویت‌ها.
- ساده‌سازی مخازن پایگاه داده به واکشی، قفل‌گذاری، مدیریت همزمانی و ذخیره‌سازی.
- نگارش مجموعه آزمون‌های ایزوله قواعد دامنه (`DomainStateTransitionTests.cs`).

### گام ۶: رفع کامل خطاهای تله‌متری ممیزی، رویدادهای دامنه و ساختار صفحات
1. **رفع افت سایلنت لاگ‌های ممیزی در `StockReservationSlices.cs`:**
   - افزودن پراپرتی‌های `OperationRecorder` و `OpContext` به `InventorySliceSupport`.
   - ارسال کانتکست معتبر عملیات و ثبات لاگ به جای مقادیر `null, null` در متد `RecordIfRejectedAsync`.
2. **اصلاح فعالیت لغزان سشن (`Sliding Activity`) و ایمنی نخ رابط کاربری:**
   - در `RuntimeSessions.ValidateAsync`، زمان آخرین فعالیت (`LastActivityAtUtc`) در حافظه رفرش شده و در فواصل بیشتر از ۲ دقیقه در دیتابیس پایدار می‌شود تا کاربران فعال Blazor با انقضای کاذب `IdleTimeoutMinutes` مواجه نشوند.
   - در `MainLayout.razor`، فراخوانی جاوااسکریپت `clearDrafts` درون `await InvokeAsync(...)` با بلوک try/catch محصور شد تا رویدادهای ابطال سشن پس‌زمینه با خطای Blazor Dispatcher Thread-Affinity مواجه نشوند.
3. **یکپارچه‌سازی رویدادهای دامنه و افزودن لاگ ممیزی خرید:**
   - حذف رکوردهای تکراری `*DomainEvent` از `ProductionOrder.cs`، `SalesOrder.cs` و `QualityInspection.cs` و استانداردسازی رویدادهای منفرد کانونی با اینترفیس `IAuditableDomainEvent`.
   - تبدیل رویدادهای خرید (`PurchaseOrderApprovedEvent`, `PurchaseOrderClosedEvent`, `PurchaseOrderCancelledEvent`) به `IAuditableDomainEvent` و پیاده‌سازی `ProcurementAuditHandler.cs` (ثبت‌شده در DI) جهت ثبت خودکار وقایع ممیزی تدارکات.
   - گسترش `SalesOrderAuditHandler.cs` جهت ثبت وقایع لغو و بستن سفارش فروش.
4. **حذف کامل ارجاعات رشته‌ای جادویی `"RowVersion"` (گاردریل ۲):**
   - پیاده‌سازی اینترفیس `IHasRowVersion` در `ContextDimension` و `BusinessPartner`.
   - بازنویسی کوئری‌های همزمانی در `BomOperationsStore.cs` و `SqlBusinessPartnerStore.cs` با استفاده از پراپرتی تایپ‌شده `IHasRowVersion.RowVersion`.
5. **اصلاح تزریق وابستگی در مخزن داشبورد و جلوگیری از نشت حافظه (Leak Defense):**
   - اصلاح سازنده `SqlDashboardQueryStore` جهت دریافت سرویس‌های `IReportingQueryStore` و `IRoutingOperationsStore` از طریق DI و ثبت سرویس‌ها در کانتینر DI.
   - بازنویسی کامپوننت `PartnerPicker.razor` به عنوان `IDisposable` با جایگزینی `CancellationTokenSource` رهاشده با کنترلر بهینه‌شده `DebouncedSearchController`.
6. **موازی‌سازی واکشی اسکوپ‌ها و استریم خروجی CSV ممیزی:**
   - موازی‌سازی ۱۸ فراخوانی متوالی `Scopes.Load*Async` در `MainLayout.razor` با استفاده از `Task.WhenAll`.
   - جایگزینی بافرینگ رشته‌های Base64 در SignalR با فراخوانی مستقیم اندپوینت استریمینگ امن HTTP در `Audit.razor` (`/api/export/audit`) از طریق `downloadFileFromUrl`.
7. **تفکیک صفحات سنگین Blazor به کامپوننت‌های کمتر از ۸۰۰ خط (گاردریل ۵):**
   - استخراج ماژول شبیه‌ساز و انفجار درخت محصول (BOM Simulator & Explosion Panel) از صفحه `Boms.razor` به کامپوننت ماژولار `BomSimulatorPanel.razor` (۳۵۷ خط).
   - کاهش حجم `Boms.razor` به ۷۰۹ خط (زیر حد مجاز ۸۰۰ خط).
   - تأیید و انطباق صفحه `Sales.razor` با ۶۹۹ خط و `Security.razor` با ۷۱۰ خط (تمامی صفحات زیر سقف ۸۰۰ خط).

### گام ۷: سخت‌سازی امنیتی احراز هویت، کلیدهای عبور (WebAuthn/Passkey)، 2FA و کنترل هدرها (F-01 تا F-24)
1. **اعمال اجباری User Verification (UV) و Step-Up Auth در کلیدهای عبور (F-01, F-07):**
   - در `WebAuthnVerifier.cs`، پرچم User Verification (`flags & 0x04 != 0`) در کنار User Presence الزامی شد.
   - در `AuthenticationBackend.cs`، مقدار `userVerification` در گزینه‌های ثبت و ورود به `"required"` ارتقا یافت.
   - شناسه و مبدا معتبر (`rp.id` و `expectedOrigin`) به تنظیمات سرور و `AllowedHosts` متصل شد تا به هدرهای ورودی اعتماد نشود.
   - ایجاد مخزن حافظه‌ای تک‌مصرفه `PasskeyChallengeStore` با انقضای زمان‌بندی‌شده جهت جلوگیری قطعی از حملات Replay.
   - استفاده از شناسه رندوم کدر ۳۲ بایتی مشتق از DataProtection برای `user.id` در WebAuthn به جای UserId عددی دیتابیس.
   - الزام ورود مجدد رمز عبور (Step-Up Auth) قبل از دریافت چالش ثبت کلید عبور در فرم و دیالوگ کاربری `Security.razor`.
2. **سخت‌سازی زمان‌بندی لاگین، تاخیر فزاینده و مقابله با DoS قفل اکانت (F-04, F-23):**
   - در `RuntimeSessions.cs`، اجرای هش PBKDF2 با اکانت و رمز مصنوعی در زمان عدم وجود یا غیرفعال بودن کاربر جهت ایجاد پاسخ زمانی ثابت و جلوگیری از نشت نام کاربری.
   - جداسازی اعتبارسنجی هش رمز عبور از تراکنش `Serializable` جهت جلوگیری از قفل طویل سطرهای دیتابیس.
   - اعمال تاخیر تصاعدی بر اساس ترکیب نام کاربری و IP کلاینت تا از حملات مسدودسازی DoS جلوگیری شود.
   - ارزیابی کوکی دستگاه مورد اعتماد 2FA در نخستین لاگین جهت حذف لاگین مضاعف و ثبت توکن در جدول `[auth].[UserToken]`.
3. **رمزنگاری اسرار TOTP و کدهای بازیابی در دیتابیس با DataProtection (F-06):**
   - فعال‌سازی `ProtectPersonalData = true` در Identity با پیاده‌سازی سفارشی `DataProtectionPersonalDataProtector` با پیشوند `ff-dp:v1:` و پشتیبانی سازگار با رکوردهای بدون رمزنگاری پیشین.
4. **جداسازی رویداد اعلان یادآوری 2FA (F-22):**
   - افزودن رویداد مستقل `TwoFactorSetupReminder` در `NotificationEventCatalog` و استفاده از آن در هشدارهای امنیتی سشن.
5. **هدرهای فورواردشده معتبر، اصلاح Rate Limiting و امن‌سازی CSP (F-05, F-08, F-24):**
   - پیکربندی استاندارد `UseForwardedHeaders` با شبکه‌های شناخته‌شده (`KnownIPNetworks`) در `Program.cs` و اتکای مستقیم `ResolveClientIp` به `RemoteIpAddress` ایمن شده.
   - در `RateLimitingExtensions.cs`، ثبت دائم تمام پالیسی‌ها حتی در صورت `Enabled = false` با `GetNoLimiter` و انتقال کامل پیام‌های خطا به فایل‌های `.resx` با پشتیبانی کامل از زبان‌های فارسی و انگلیسی.
   - محدودسازی دقیق `connect-src` در CSP به مبدا خودی و وب‌سوکت‌های هم‌مبدا.

### گام ۸: حذف رقابت قفل‌ها، تراکنش‌های سنگین، بهبود همزمانی انبار و رفع خطاهای خاموش (F-02, F-03, F-14, F-19, F-20)
1. **حذف قفل‌گذاری سطری Serializable در اعتبارسنجی هر درخواست (F-02):**
   - قرارگیری `app.MapStaticAssets()` قبل از میدل‌ویرهای احراز هویت و سشن در `Program.cs` جهت عبور سریع فایل‌های استاتیک بدون کوئری دیتابیس.
   - پیاده‌سازی کش حافظه‌ای لغزان ۲۰ ثانیه‌ای برای پرینسیپال‌های سشن و ترجیحات کاربری بر اساس SessionId + SecurityStamp با ابطال آنی در زمان Revoke یا خروج کاربر (`ISessionInvalidator`).
   - تغییر اعتبارسنجی سشن در `RuntimeSessions.ValidateAsync` به سطح ایزولاسیون غیرمسدودکننده `READ COMMITTED` بدون قفل `UPDLOCK/HOLDLOCK` و اخذ قفل فقط هنگام ذخیره رخداد فعالیت.
2. **حذف قفل انحصاری سطری سازمان در تراکنش‌های روتین (F-03):**
   - تجمیع متدهای تکراری اعتبارسنجی سازمان در کلاس اشتراکی `OrganizationActivityGuard.cs`.
   - استفاده از بررسی غیرمسدودکننده (`AsNoTracking` یا `HOLDLOCK` اشتراکی بدون `UPDLOCK`) در کلیه مخازن عملیاتی روزمره (انبار، سفارشات فروش، خرید، کیفیت و توالی اسناد) و حفظ قفل انحصاری `LockExclusiveOrganizationAsync` صرفاً برای دستورات حیاتی مدیر سازمان.
3. **بهینه‌سازی قفل سطری موجودی کالا در انبار (F-14):**
   - ارتقای کوئری خام به‌روزرسانی مانده انبار در `SqlInventoryOperationsStore` با هدایت به ایندکس ترکیبی `(OrganizationId, WarehouseId, ItemId, LotCode)` همراه با هینت دقیق `WITH (UPDLOCK, ROWLOCK)`.
4. **اصلاح ترتیب بلوک‌های Catch در پایداری انبار (F-19):**
   - اصلاح ترتیب در متد `SqlInventoryOperationsStore.Write<T>` به گونه‌ای که خطای همزمانی `DbUpdateConcurrencyException` قبل از خطای عمومی `DbUpdateException` ارزیابی و مدیریت شود.
5. **جایگزینی بلوک‌های Catch خاموش با لاگینگ ساختاریافته (F-20):**
   - جایگزینی بلوک‌های خالی `catch { }` در کامپوننت‌های رابط کاربری (`UserProfileSection.razor`, `SalesOrders.razor`, `DeliveryOrders.razor`) و `AuthenticationBackend.cs` با استخراج نوع خطای مشخص و ثبت در لاگ سیستم (`ILogger`).
6. **تضمین انتشار کانتکست چندمستأجری (`CurrentOrganizationId`):**
   - انتشار صریح `organizationId` در تمامی فراخوانی‌های پایگاه داده و مقداردهی `db.CurrentOrganizationId` پیش از اعمال فیلترهای سراسری.

### گام ۹: آماده‌سازی استقرار در محیط پروداکشن، پایش سلامت و پاکسازی ابزارها (F-09, F-11, F-17, F-18, F-26, F-27, F-28, F-29, F-30)
1. **ریدایرکت اجباری HTTPS و امنیت HSTS (F-09):**
   - افزودن `app.UseHttpsRedirection()` در `Program.cs` همراه با `app.UseHsts()` در محیط‌های غیر توسعه تا کوکی‌های امن با پیشوند `__Host-` همواره تضمین شوند.
2. **پایداری کلیدهای DataProtection در پروداکشن و Fail-Fast (F-28, F-30):**
   - در `LocalRuntimeConfiguration.cs`، در صورت قرارگیری در محیط `Production`، مقدار `DataProtection:KeyRingPath` الزامی شده و در صورت عدم تنظیم، استارت‌آپ برنامه با استثنای صریح متوقف می‌شود (Fail-Fast) و از سقوط به کلیدهای موقت حافظه جلوگیری می‌گردد.
   - به‌روزرسانی اسکریپت `Run-ForgeFlow-Server.bat` با پیش‌فرض محیط پروداکشن، اتصال اختصاصی به HTTPS (پورت ۵۰۰۱)، اعتبارسنجی گواهی SSL و مسیر ذخیره کلیدهای پایدار.
3. **ثبت Health Checks و سقف‌گذاری منابع مدارهای Blazor Circuit (F-29):**
   - ثبت چک‌های سلامت اتصالات پایگاه داده (`OrganizationDbContext` و `AuthDbContext`) و اندپوینت عمومی سبک `/health` با پاسخ تأییدشده `Healthy` و وضعیت `200 OK`.
   - ایجاد چک سلامت سفارشی فضای دیسک (`DiskSpaceHealthCheck`) برای نظارت بر درایو حاوی لاگ‌های سیستم با وضعیت `Degraded` در زیر ۱ گیگابایت و `Unhealthy` در زیر ۲۵۰ مگابایت.
   - اعمال محدودیت‌های صریح بر منابع مدارهای Blazor (`DisconnectedCircuitMaxRetained = 100`، `DisconnectedCircuitRetentionPeriod = 3min`، `MaxBufferedUnacknowledgedRenderBatches = 10`) در `Program.cs` جهت جلوگیری از مصرف بی‌رویه حافظه در بار بالا.
4. **شفاف‌سازی امنیت پیش‌نویس‌ها، کد کشور ISO و ساعت سازمانی (F-17, F-26):**
   - در `drafts.mjs`، مستندسازی صریح گردید که ذخیره پیش‌نویس صرفاً از تکنیک درهم‌ریزی (Obfuscation) جهت جلوگیری از مشاهده تصادفی در `sessionStorage` استفاده می‌کند و ادعای رمزنگاری قوی ندارد. نام متدها به `obfuscateDraft` و `deobfuscateDraft` تغییر یافت و استثنای فیلدهای حساس با `isSensitive()` تضمین شد.
   - در `BusinessPartners.razor`، کد استاندارد بین‌المللی کشور (`"IR"`) به عنوان مقدار پیش‌فرض تجاری جایگزین متن هاردکد شده فارسی گردید و نام نمایشی کشور از طریق کلید `Country_IR` در فایل‌های `.resx` بارگذاری می‌شود.
   - در صفحات `Audit.razor` و `Errors.razor`، ماکزیمم تاریخ مجاز انتخابگر تاریخ (`MaxDate`) به ساعت و منطقه زمانی سازمان (`IOrganizationClock`) متصل شد تا از وابستگی به ساعت ماشین سرور جلوگیری شود.
5. **پاکسازی کدهای مرده، تجمیع پالیسی‌های Rate-Limiter و جداسازی ابزارهای تست (F-11, F-18, F-27):**
   - فیلد استفاده‌نشده `editingSpec` و دستورات خاموش‌سازی اخطار کامپایلر (`#pragma warning disable CS0414`) از `Quality.razor` حذف شد.
   - ثوابت تکراری `LegacyAuth` حذف و کلیه اندپوینت‌ها و آزمون‌ها بر روی ثابت واحد `RateLimitingPolicyNames.Auth` تجمیع شدند.
   - ابزارهای هاست محلی `HostLocalTestAccountCreator` و `HostRecovery` از کتابخانه پروداکشن `ForgeFlow.Infrastructure` خارج و به پروژه‌های اختصاصی `tools/ForgeFlow.LocalTestAccount` و `tools/ForgeFlow.LocalTestRecovery` منتقل شدند.
   - اسکریپت بسته‌بندی مخزن `22.bat` ایجاد گردید تا در زمان ساخت آرشیو، پوشه‌های کلیدهای لوکال (`.local/`) و فایل‌های موقت استخراج شوند و تاریخچه Migrationهای دیتابیس برای ممیزی دست‌نخورده حفظ گردند.

---

## ۳. خلاصه نتایج اعتبارسنجی (Verification Evidence)

| شاخص ارزیابی | نتیجه حاصله | وضعیت |
| :--- | :--- | :---: |
| **بیلد سولوشن (Release)** | `dotnet build -c Release` بدون هیچ اخطار یا خطا (`0 Warnings, 0 Errors`) | ✅ موفق |
| **مجموعه آزمون‌های خودکار و یکپارچگی SQL** | `dotnet test`: مجموع ۸۱۹ آزمون (**۸۱۸ پاس شده**، ۰ شکست، ۱ معلق) با شناسایی خودکار سرور | ✅ ۱۰۰٪ سبز |
| **پایش سلامت سرویس (Health Checks)** | فراخوانی `GET /health` با نتیجه `200 OK` و خروجی `Healthy` (پایگاه‌داده و فضای دیسک) | ✅ تأیید شد |
| **سخت‌سازی DataProtection در پروداکشن** | بررسی پرتاب استثنای Fail-Fast در زمان عدم تنظیم کلید و پیکربندی خودکار مسیر پایدار | ✅ تأیید شد |
| **آزمون‌های همزمانی چندمستأجری** | اجرای موفق کلیه تست‌های همزمانی رزرو، صدور و ورود کالا، توالی اسناد و تخصیص همزمان نقش‌ها | ✅ تأیید شد |
| **آزمون‌های امنیتی کلید عبور** | اعتبارسنجی موفق User Verification، چالش‌های تک‌مصرفه، Step-Up Auth و ریپلای توکن | ✅ تأیید شد |
| **جداسازی ابزارهای تست از کتابخانه هسته** | حذف کامل `HostRecovery` و `HostLocalTestAccountCreator` از اسمبلی زیرساخت پروداکشن | ✅ تأیید شد |
| **شفافیت پیش‌نویس و کد کشور ISO** | درهم‌ریزی بدون ادعای کریپتوگرافی قوی، ذخیره `"IR"` و ترجمه صریح با `.resx` | ✅ تأیید شد |
| **ساعت و تقویم سازمانی در فیلترها** | انطباق `MaxDate` در گزارش‌های ممیزی و لاگ‌های خطا با `IOrganizationClock` | ✅ تأیید شد |
| **اسکریپت بسته‌بندی مخزن (`22.bat`)** | ساخت آرشیو امن با حذف `.local/` و فایل‌های موقت و حفظ کامل پوشه‌های Migration | ✅ تأیید شد |
| **سرویس وب محلی** | پورت ۵۰۰۱ (HTTPS) با پاسخ تأییدشده **HTTP 200** روی `/account/login` و `/health` | ✅ لایو و پایدار |

---

## ۴. ماتریس تطبیق نهایی با گاردریل‌های معماری (Architectural Guardrails Matrix)

| شماره گاردریل | شرح قانون و گاردریل | وضعیت در کد | نحوه تحقق و اعتبارسنجی |
| :---: | :--- | :---: | :--- |
| **FFA-001** | ساختار اسلایس‌های عمودی در لایه Application | ✅ رعایت شد | تفکیک کامل UseCases به اسلایس‌های مجزا با لایه Facade سبک |
| **FFA-002** | همزمانی خوش‌بینانه و حذف Magic Strings برای RowVersion | ✅ رعایت شد | پیاده‌سازی تایپ‌شده `IHasRowVersion` و حذف رشته‌های جادویی در تمام مخازن |
| **FFA-003** | ثبت دقیق رخدادهای دامنه‌ای و ممیزی رویدادهای کسب‌وکار | ✅ رعایت شد | رویدادهای کانونی با `IAuditableDomainEvent` و فعال‌سازی `ProcurementAuditHandler` |
| **FFA-004** | استریمینگ فایل‌ها و داده‌های بزرگ به جای بافرینگ SignalR | ✅ رعایت شد | دانلود فایل‌های CSV با `IAsyncEnumerable` و استریم مستقیم HTTP |
| **FFA-005** | تجزیه کامپوننت‌های بزرگ Razor به کامپوننت‌های کمتر از ۸۰۰ خط | ✅ رعایت شد | تفکیک شبیه‌ساز درخت محصول به `BomSimulatorPanel.razor` و نگه‌داشتن تمامی صفحات زیر ۸۰۰ خط |
| **FFA-006** | بهینه‌سازی جستجو با Debounce و مدیریت چرخه حیات توکن‌ها | ✅ رعایت شد | پیاده‌سازی `IDisposable` و `DebouncedSearchController` در `PartnerPicker.razor` |
| **FFA-007** | مدیریت صحیح نشست‌ها و به‌روزرسانی فعال زمان انقضای هرزمان | ✅ رعایت شد | رفرش حافظه‌ای و پایدارسازی دوره‌ای `LastActivityAtUtc` در `RuntimeSessions` |
| **FFA-008** | ایمنی نخ‌های Blazor Dispatcher در تعاملات کلاینت | ✅ رعایت شد | محصورسازی فراخوانی‌های JS در `await InvokeAsync(...)` در `MainLayout.razor` |
| **FFA-009** | تفکیک قطعی مرزهای دیتابیس `AuthDbContext` و `OrganizationDbContext` | ✅ رعایت شد | تفکیک با Marker Interfaces و حذف تمام `modelBuilder.Ignore` |
| **FFA-010** | عدم نشت منطق کسب‌وکار به لایه Infrastructure و کپسوله‌سازی Domain | ✅ رعایت شد | هدایت تمامی قوانین اعتبارسنجی، ماشین وضعیت و محاسبات به درون ریشه‌های تجمیعی |
| **FFA-011** | الزام User Verification و Step-Up Auth در WebAuthn/Passkey | ✅ رعایت شد | بررسی پرچم 0x04، الزام رمز جاری در ثبت، چالش تک‌مصرفه در سرور |
| **FFA-012** | رمزنگاری کلیدهای حساس احراز هویت دو مرحله‌ای در دیتابیس | ✅ رعایت شد | پیاده‌سازی `DataProtectionPersonalDataProtector` برای TOTP و کدهای بازیابی |
| **FFA-013** | مقابله با نشت زمان‌بندی لاگین و حملات انکار سرویس قفل اکانت | ✅ رعایت شد | اجرای هش PBKDF2 مصنوعی برای کاربران ناموجود و تاخیر فزاینده ترکیبی با IP |
| **FFA-014** | ایمن‌سازی پروکسی‌ها با هدرهای فورواردشده، Rate Limiting و CSP سخت‌گیرانه | ✅ رعایت شد | پالیسی‌های بدون وقفه، هدرهای IP معتبر، محلی‌سازی کامل پیام‌ها در resx و CSP تمیز |
| **FFA-015** | رفع رقابت قفل‌های پایگاه داده و بهینه‌سازی تراکنش‌ها | ✅ رعایت شد | کشینگ لغزان سشن، حذف Serializable در درخواست‌های معمول، قفل ردیفی دقیق در انبار و محافظت از فعالیت سازمان |
| **FFA-016** | سخت‌سازی استقرار در پروداکشن، پایش سلامت و انزوای ابزارهای توسعه | ✅ رعایت شد | ریدایرکت HTTPS، کلیدهای پایدار DataProtection، اندپوینت `/health` با پایش دیسک، خروج ابزارهای تست از لایه زیرساخت |
