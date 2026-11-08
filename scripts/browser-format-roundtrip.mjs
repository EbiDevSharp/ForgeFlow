import { chromium, expect } from '../.tools/browser/node_modules/@playwright/test/index.mjs';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, 'verification/format-roundtrip-09');
fs.mkdirSync(output, { recursive: true });
function resource(culture) {
  const xml = fs.readFileSync(path.join(root, `src/ForgeFlow.Web/Resources/Localization/SampleResources.${culture}.resx`), 'utf8');
  return Object.fromEntries([...xml.matchAll(/<data name="([^"]+)"[^>]*><value>([^<]*)<\/value><\/data>/g)].map(match => [match[1], match[2]]));
}
const text = { 'fa-IR': resource('fa-IR'), 'en-US': resource('en-US') };
const listener = net.createServer();
await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
const port = listener.address().port;
await new Promise(resolve => listener.close(resolve));
const base = `http://127.0.0.1:${port}`;
const runDirectory = path.join(root, '.tools/smoke-runs', randomUUID());
fs.mkdirSync(runDirectory, { recursive: true });
const log = fs.openSync(path.join(output, 'server.log'), 'w');
const server = spawn(path.join(root, '.tools/dotnet/dotnet.exe'),
  [path.join(root, '.tools/format-roundtrip-output/ForgeFlow.Web.dll'), '--urls', base], {
    cwd: path.join(root, 'src/ForgeFlow.Web'), stdio: ['ignore', log, log],
    env: { ...process.env, DOTNET_ROOT: path.join(root, '.tools/dotnet'), DOTNET_CLI_HOME: path.join(root, '.tools/cli-home'),
      NUGET_PACKAGES: path.join(root, '.tools/packages'), DOTNET_ENVIRONMENT: 'SmokeTest', ASPNETCORE_ENVIRONMENT: 'SmokeTest',
      ForgeFlow__Smoke__KeyDirectory: path.join(runDirectory, 'keys') }
  });
let browser;
const evidence = { environment: 'SmokeTest', browser: null, cases: [], failures: [] };
async function ready(page, culture) {
  await expect(page.locator('#sample-name')).toBeEnabled({ timeout: 20000 });
  await expect(page.locator('html')).toHaveAttribute('lang', culture);
  await expect(page.locator('html')).toHaveAttribute('dir', culture === 'fa-IR' ? 'rtl' : 'ltr');
}
async function switchTo(page, culture) {
  await page.locator(culture === 'fa-IR' ? '#language-fa' : '#language-en').click();
  await ready(page, culture);
}
async function noOverflow(page, width, culture) {
  const layout = await page.evaluate(() => ({ viewport: innerWidth, document: document.documentElement.scrollWidth,
    mainDirection: getComputedStyle(document.querySelector('main')).direction,
    dateDirection: getComputedStyle(document.querySelector('#sample-date')).direction }));
  if (layout.document > width || layout.mainDirection !== (culture === 'fa-IR' ? 'rtl' : 'ltr'))
    throw new Error(`Layout failed at ${width}px/${culture}: ${JSON.stringify(layout)}`);
  return layout;
}

try {
  for (let attempt = 0; ; attempt++) {
    try { if ((await fetch(base)).ok) break; } catch { }
    if (attempt > 120 || server.exitCode !== null) throw new Error('SmokeTest server readiness failed');
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] });
  evidence.browser = `Microsoft Edge ${browser.version()}`;

  for (const width of [1440, 320]) {
    const context = await browser.newContext({ locale: 'fa-IR', viewport: { width, height: 950 } });
    const page = await context.newPage();
    await page.goto(base);
    await ready(page, 'fa-IR');

    await page.locator('#sample-name').fill('Round trip نام');
    await page.locator('#sample-date').fill('۱۴۰۴/۰۱/۰۱');
    await page.locator('#sample-amount').fill('۱۲۳۴٫۵۰');
    await page.locator('#sample-currency').selectOption('EUR');
    await page.locator('#sample-mode').selectOption('detailed');
    await page.locator('#sample-notes').fill('Draft one\nیادداشت دو');
    await expect(page.locator('#date-preview')).toHaveText('1404/01/01');
    await expect(page.locator('#sample-date')).toHaveAttribute('data-input-culture', 'fa-IR');
    await expect(page.locator('#sample-amount')).toHaveAttribute('data-input-culture', 'fa-IR');
    await expect(page.locator('#money-preview')).toContainText('EUR');

    await switchTo(page, 'en-US');
    await expect(page.locator('#sample-date')).toHaveValue('۱۴۰۴/۰۱/۰۱');
    await expect(page.locator('#sample-amount')).toHaveValue('۱۲۳۴٫۵۰');
    await expect(page.locator('#sample-currency')).toHaveValue('EUR');
    await expect(page.locator('#sample-mode')).toHaveValue('detailed');
    await expect(page.locator('#date-preview')).toHaveText('2025/03/21');
    await expect(page.locator('#sample-date')).toHaveAttribute('data-input-culture', 'fa-IR');
    await expect(page.locator('#amount-preview')).toHaveText('1,234.50');
    const enLayout = await noOverflow(page, width, 'en-US');
    await page.screenshot({ path: path.join(output, `roundtrip-en-US-${width}.png`), fullPage: true });

    await page.locator('#sample-date').fill('03/21/2025');
    await page.locator('#sample-amount').fill('1234.50');
    await page.locator('#sample-currency').selectOption('EUR');
    await page.locator('#sample-mode').selectOption('detailed');
    await expect(page.locator('#sample-date')).toHaveAttribute('data-input-culture', 'en-US');
    await expect(page.locator('#sample-amount')).toHaveAttribute('data-input-culture', 'en-US');
    await switchTo(page, 'fa-IR');
    await expect(page.locator('#sample-date')).toHaveValue('03/21/2025');
    await expect(page.locator('#sample-amount')).toHaveValue('1234.50');
    await expect(page.locator('#sample-currency')).toHaveValue('EUR');
    await expect(page.locator('#sample-mode')).toHaveValue('detailed');
    await expect(page.locator('#date-preview')).toHaveText('1404/01/01');
    await expect(page.locator('#sample-date')).toHaveAttribute('data-input-culture', 'en-US');
    await expect(page.locator('#amount-preview')).toHaveText('1٬234٫50');
    await expect(page.locator('#sample-notes')).toHaveValue('Draft one\nیادداشت دو');
    const layout = await noOverflow(page, width, 'fa-IR');
    await page.screenshot({ path: path.join(output, `roundtrip-fa-IR-${width}.png`), fullPage: true });

    await page.reload();
    await ready(page, 'fa-IR');
    await expect(page.locator('#sample-name')).toHaveValue('Round trip نام');
    await expect(page.locator('#sample-date')).toHaveValue('03/21/2025');
    await expect(page.locator('#sample-amount')).toHaveValue('1234.50');
    await expect(page.locator('#sample-currency')).toHaveValue('EUR');
    await expect(page.locator('#sample-mode')).toHaveValue('detailed');
    await expect(page.locator('#sample-notes')).toHaveValue('Draft one\nیادداشت دو');

    await page.locator('#sample-date').fill('1404/13/01');
    await page.locator('#sample-amount').fill('۱۲۳۴.۵۰');
    await page.locator('#sample-submit').click();
    await expect(page.locator('#date-error')).toHaveText(text['fa-IR'].DateInvalid);
    await expect(page.locator('#amount-error')).toHaveText(text['fa-IR'].AmountInvalid);
    await expect(page.locator('#sample-date')).toHaveValue('1404/13/01');
    await expect(page.locator('#sample-amount')).toHaveValue('۱۲۳۴.۵۰');
    await switchTo(page, 'en-US');
    await expect(page.locator('#date-error')).toHaveText(text['en-US'].DateInvalid);
    await expect(page.locator('#amount-error')).toHaveText(text['en-US'].AmountInvalid);
    await expect(page.locator('#sample-date')).toHaveValue('1404/13/01');
    await expect(page.locator('#sample-amount')).toHaveValue('۱۲۳۴.۵۰');
    await switchTo(page, 'fa-IR');
    await expect(page.locator('#date-error')).toHaveText(text['fa-IR'].DateInvalid);
    await expect(page.locator('#amount-error')).toHaveText(text['fa-IR'].AmountInvalid);

    await page.locator('#sample-date').fill('۱۴۰۴/۰۱/۰۱');
    await page.locator('#sample-amount').fill('۱۲۳۴٫۵۰');
    await page.locator('#sample-submit').click();
    await expect(page.locator('#canonical-values')).toBeVisible();
    await expect(page.locator('#submitted-date')).toHaveText('2025-03-21');
    await expect(page.locator('#submitted-amount')).toHaveText('1234.5');
    await expect(page.locator('#submitted-currency')).toHaveText('EUR');
    await expect(page.locator('#submitted-mode')).toHaveText('detailed');
    const keysAfterSubmit = await page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('forgeflow.draft:')));
    if (keysAfterSubmit.length !== 0) throw new Error('Successful submission did not clear the extended draft');
    await page.reload();
    await ready(page, 'fa-IR');
    await expect(page.locator('#sample-date')).toHaveValue('');
    await expect(page.locator('#sample-amount')).toHaveValue('0');
    await expect(page.locator('#sample-currency')).toHaveValue('USD');
    await expect(page.locator('#sample-mode')).toHaveValue('summary');
    if ((await page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('forgeflow.draft:')))).length)
      throw new Error('Successful submission draft returned after reload');

    await page.locator('#sample-date').fill('۱۴۰۴/۰۱/۰۱');
    await page.locator('#sample-amount').fill('۲۵٫۰۰');
    await page.locator('#sample-currency').selectOption('IRR');
    await page.locator('#sample-mode').selectOption('summary');
    await page.locator('#sample-discard').click();
    await expect(page.locator('#draft-status')).toHaveText(text['fa-IR'].DraftDiscarded);
    if ((await page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('forgeflow.draft:')))).length)
      throw new Error('Explicit discard did not clear the extended draft');
    await page.reload();
    await ready(page, 'fa-IR');
    await expect(page.locator('#sample-date')).toHaveValue('');
    await expect(page.locator('#sample-currency')).toHaveValue('USD');

    if (width === 1440) {
      const tab = await page.evaluate(() => sessionStorage.getItem('forgeflow.tab'));
      await page.evaluate(tabId => {
        const key = `forgeflow.draft:anonymous:/:sample:${tabId}`;
        sessionStorage.setItem(key, JSON.stringify({ version: 1, scope: 'anonymous:/', savedAt: Date.now(), snapshot: {
          fields: { Name: { raw: 'Old version', inputCulture: null }, AmountRaw: { raw: '1.00', inputCulture: 'en-US' }, Notes: { raw: '', inputCulture: null } },
          dirty: ['Name'], touched: [], validationAttempted: false, focusId: null
        } }));
      }, tab);
      await page.reload();
      await ready(page, 'fa-IR');
      await expect(page.locator('#draft-status')).toHaveText(text['fa-IR'].DraftRejected);
      await expect(page.locator('#sample-date')).toHaveValue('');
      if ((await page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('forgeflow.draft:')))).length)
        throw new Error('Version 1 draft was not safely rejected and removed');
    }

    evidence.cases.push({ width, languages: ['fa-IR', 'en-US'], layout: { fa: layout, en: enLayout },
      cultureSwitchAndReload: 'all fields and input cultures preserved',
      validation: 'invalid Jalali date and localized numeric input retained; errors relocalized FA→EN→FA',
      canonicalSubmission: { date: '2025-03-21', amount: '1234.5', currency: 'EUR', mode: 'detailed' },
      submitAndDiscardClear: 'passed', olderVersionPolicy: width === 1440 ? 'version 1 rejected and cleared' : 'covered at 1440px' });
    await context.close();
  }
} catch (error) {
  evidence.failures.push(error.stack ?? String(error));
  process.exitCode = 1;
} finally {
  await browser?.close(); server.kill(); fs.closeSync(log);
  fs.writeFileSync(path.join(output, 'browser-results.json'), JSON.stringify(evidence, null, 2));
}
console.log(JSON.stringify(evidence, null, 2));
