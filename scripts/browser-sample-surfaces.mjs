import { chromium, expect } from '../.tools/browser/node_modules/@playwright/test/index.mjs';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, 'verification/sample-surfaces-07');
fs.mkdirSync(output, { recursive: true });
function readResource(culture) {
  const source = fs.readFileSync(path.join(root, `src/ForgeFlow.Web/Resources/Localization/SampleResources.${culture}.resx`), 'utf8');
  return Object.fromEntries([...source.matchAll(/<data name="([^"]+)"[^>]*><value>([^<]*)<\/value><\/data>/g)].map(match => [match[1], match[2].replaceAll('&quot;', '"').replaceAll('&amp;', '&')]));
}
const resources = { 'fa-IR': readResource('fa-IR'), 'en-US': readResource('en-US') };
const listener = net.createServer();
await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
const port = listener.address().port;
await new Promise(resolve => listener.close(resolve));
const base = `http://127.0.0.1:${port}`;
const runDirectory = path.join(root, '.tools/smoke-runs', randomUUID());
fs.mkdirSync(runDirectory, { recursive: true });
const log = fs.openSync(path.join(output, 'server.log'), 'w');
const server = spawn(path.join(root, '.tools/dotnet/dotnet.exe'),
  [path.join(root, '.tools/sample-surfaces-output/ForgeFlow.Web.dll'), '--urls', base], {
    cwd: path.join(root, 'src/ForgeFlow.Web'), stdio: ['ignore', log, log],
    env: { ...process.env, DOTNET_ROOT: path.join(root, '.tools/dotnet'), DOTNET_CLI_HOME: path.join(root, '.tools/cli-home'),
      NUGET_PACKAGES: path.join(root, '.tools/packages'), DOTNET_ENVIRONMENT: 'SmokeTest', ASPNETCORE_ENVIRONMENT: 'SmokeTest',
      ForgeFlow__Smoke__KeyDirectory: path.join(runDirectory, 'keys') }
  });
let browser;
const evidence = { environment: 'SmokeTest', surfaces: ['table', 'tooltip', 'loading-empty', 'notification', 'localization-only-error-code'], checks: [], failures: [] };
try {
  for (let i = 0; ; i++) {
    try { if ((await fetch(base)).ok) break; } catch { }
    if (i > 120) throw new Error('Server readiness failed');
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] });
  evidence.browser = browser.version();
  for (const culture of ['fa-IR', 'en-US']) for (const width of [1440, 320]) {
    const context = await browser.newContext({ locale: culture, viewport: { width, height: 900 } });
    await context.addCookies([{ name: '.AspNetCore.Culture', value: `c=${culture}|uic=${culture}`, url: base }]);
    const page = await context.newPage();
    await page.goto(base);
    const text = resources[culture];
    await expect(page.locator('#sample-name')).toBeEnabled({ timeout: 20000 });
    const table = page.locator('#sample-table');
    await expect(table.locator('caption')).toHaveText(text.TableCaption);
    await expect(table.locator('thead')).toContainText(text.TableFieldHeading);
    await expect(table.locator('tbody tr')).toHaveCount(2);
    const direction = await page.locator('html').getAttribute('dir');
    if (direction !== (culture === 'fa-IR' ? 'rtl' : 'ltr')) throw new Error(`Unexpected page direction ${direction}`);
    const headerBoxes = await table.locator('thead th').evaluateAll(items => items.map(item => item.getBoundingClientRect().x));
    if (direction === 'rtl' ? headerBoxes[0] < headerBoxes[1] : headerBoxes[0] > headerBoxes[1])
      throw new Error(`Table columns are not placed in ${direction} order: ${JSON.stringify(headerBoxes)}`);

    const tooltipButton = page.getByRole('button', { name: text.TooltipButton });
    await tooltipButton.focus();
    const tooltip = page.getByRole('tooltip');
    await expect(tooltip).toBeVisible();
    await expect(tooltip).toContainText(text.TooltipContent);
    const describedBy = await tooltipButton.getAttribute('aria-describedby');
    if (!describedBy) throw new Error('Keyboard tooltip trigger has no accessible description relationship');

    await page.locator('#sample-refresh').click();
    await expect(page.locator('#sample-loading')).toHaveText(text.SampleLoading);
    await expect(page.locator('#sample-loading')).toHaveAttribute('aria-live', 'polite');
    await expect(page.locator('#sample-empty')).toHaveText(text.SampleEmpty, { timeout: 5000 });
    await expect(table.locator('tbody tr')).toHaveCount(0);
    await page.locator('#sample-refresh').click();
    await expect(page.locator('#sample-loading')).toBeVisible();
    await expect(table.locator('tbody tr')).toHaveCount(2, { timeout: 5000 });

    await page.locator('#sample-notify').focus();
    await page.keyboard.press('Enter');
    const notification = page.getByRole('status').filter({ hasText: text.NotificationContent });
    await expect(notification).toContainText(text.NotificationTitle);
    if (await notification.evaluate(element => getComputedStyle(element).direction) !== direction)
      throw new Error('Notification direction does not follow the selected culture');
    const dismiss = page.getByRole('button', { name: text.MessageDismiss }).first();
    await expect(dismiss).toBeVisible();
    await dismiss.focus();
    await page.keyboard.press('Enter');
    await expect(notification).toHaveCount(0);

    await page.locator('#sample-identity-error').focus();
    await page.keyboard.press('Enter');
    const error = page.getByRole('alert');
    await expect(error).toContainText(text.IdentityFixtureTitle);
    await expect(error).toContainText(text.IdentityErrorDuplicateUserName.replace('{0}', 'sample-user'));
    await expect(page.getByText(text.IdentityFixtureScope)).toBeVisible();
    if (await error.evaluate(element => getComputedStyle(element).direction) !== direction)
      throw new Error('Error fixture direction does not follow the selected culture');
    await page.getByRole('button', { name: text.MessageDismiss }).click();
    await expect(error).toHaveCount(0);

    const layout = await page.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth,
      tableWidth: document.querySelector('#sample-table').getBoundingClientRect().width,
      mainDirection: getComputedStyle(document.querySelector('main')).direction,
      tableDirection: getComputedStyle(document.querySelector('#sample-table')).direction }));
    if (layout.scrollWidth > width || layout.tableWidth > width || layout.mainDirection !== direction || layout.tableDirection !== direction)
      throw new Error(`Overflow or direction mismatch: ${JSON.stringify(layout)}`);
    await page.screenshot({ path: path.join(output, `surfaces-${culture}-${width}.png`), fullPage: true });
    evidence.checks.push({ culture, width, direction, layout, tooltip: 'keyboard focus, localized content and aria-describedby passed',
      table: 'localized caption/headings and 2 rows passed', loadingEmpty: 'loading and empty transitions passed',
      notification: 'localized status announced and dismissible', errorFixture: 'localized parameterized alert; localization-only label visible' });
    await context.close();
  }
} catch (error) { evidence.failures.push(error.stack); process.exitCode = 1; }
finally {
  await browser?.close(); server.kill(); fs.closeSync(log);
  fs.writeFileSync(path.join(output, 'browser-results.json'), JSON.stringify(evidence, null, 2));
}
console.log(JSON.stringify(evidence, null, 2));
