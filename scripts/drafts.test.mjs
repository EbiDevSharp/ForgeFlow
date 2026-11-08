// Dedicated fixtures for the reusable browser draft envelope, without a DOM.
import test from 'node:test';
import assert from 'node:assert/strict';
import { decode } from '../src/ForgeFlow.Web/wwwroot/js/drafts.mjs';

const now = 1800000000000;
const definition = { version: 2, scope: 'anonymous:/', fields: [
    { name: 'Name', id: 'sample-name', maxLength: 100, kind: 'text' },
    { name: 'DateRaw', id: 'sample-date', maxLength: 32, kind: 'date' },
    { name: 'AmountRaw', id: 'sample-amount', maxLength: 64, kind: 'number' },
    { name: 'CurrencyCode', id: 'sample-currency', maxLength: 3, kind: 'currency', allowedValues: ['USD', 'EUR', 'IRR'] },
    { name: 'ModeValue', id: 'sample-mode', maxLength: 16, kind: 'enum', allowedValues: ['summary', 'detailed'] },
    { name: 'Notes', id: 'sample-notes', maxLength: 500, kind: 'text' }
], cultures: ['fa-IR', 'en-US'] };
function envelope() { return { version: 2, scope: 'anonymous:/', savedAt: now, snapshot: {
    fields: { Name: { raw: 'Synthetic نام', inputCulture: null }, DateRaw: { raw: '۱۴۰۴/۰۱/۰۱', inputCulture: 'fa-IR' },
        AmountRaw: { raw: '۱۲۳۴٫۵۰', inputCulture: 'fa-IR' }, CurrencyCode: { raw: 'EUR', inputCulture: null },
        ModeValue: { raw: 'detailed', inputCulture: null }, Notes: { raw: 'Temporary note', inputCulture: null } },
    dirty: ['DateRaw', 'AmountRaw', 'CurrencyCode', 'ModeValue'], touched: ['Name'], validationAttempted: true, focusId: 'sample-amount'
} }; }
test('missing draft is a normal empty state', () => assert.equal(decode(null, definition, now).status, 'Missing'));
test('valid draft retains raw invalid input, source culture, focus and validation metadata', () => {
    const data = envelope(); assert.deepEqual(decode(JSON.stringify(data), definition, now).snapshot, data.snapshot);
});
for (const [name, mutate] of [
    ['older version 1 under version 2 schema policy', data => data.version = 1],
    ['future incompatible version', data => data.version = 3],
    ['another scope', data => data.scope = 'another-form'],
    ['expired', data => data.savedAt -= 1800001],
    ['future timestamp', data => data.savedAt += 60001],
    ['missing field', data => delete data.snapshot.fields.DateRaw],
    ['unallowed field', data => data.snapshot.fields.Password = { raw: 'fixture' }],
    ['wrong field type', data => data.snapshot.fields.Name.raw = 12],
    ['oversized field', data => data.snapshot.fields.Name.raw = 'x'.repeat(101)],
    ['unsupported numeric culture', data => data.snapshot.fields.AmountRaw.inputCulture = 'xx-XX'],
    ['unsupported date culture', data => data.snapshot.fields.DateRaw.inputCulture = 'xx-XX'],
    ['invalid currency code', data => data.snapshot.fields.CurrencyCode.raw = 'XYZ'],
    ['invalid enum value', data => data.snapshot.fields.ModeValue.raw = 'unknown'],
    ['wrong validation state', data => data.snapshot.validationAttempted = 'true'],
    ['unallowed dirty field', data => data.snapshot.dirty = ['Password']],
    ['unsafe focus target', data => data.snapshot.focusId = 'unrelated-control']
]) test(`reject ${name}`, () => { const data = envelope(); mutate(data); assert.equal(decode(JSON.stringify(data), definition, now).status, 'Rejected'); });
test('malformed, null and oversized payloads are rejected', () => {
    for (const raw of ['{broken', 'null', '[]', 'x'.repeat(16385)]) assert.equal(decode(raw, definition, now).status, 'Rejected');
});
