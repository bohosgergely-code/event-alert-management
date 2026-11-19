/**
 * End-to-end journey in a real browser against the real stack:
 *   API (with background workers) + SMTP server + Angular dev server.
 * Usage: node e2e/journey.cjs   (see e2e/README.md for how to start the stack)
 */
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const BASE = process.env.BASE_URL || 'http://127.0.0.1:4200';
const SHOTS = path.join(__dirname, '..', 'evidence', 'e2e');
const SMTP_OUT = process.env.SMTP_OUT || '/tmp/smtp-e2e';
const stamp = Date.now().toString(36);
const email = `journey-${stamp}@example.com`;

let step = 0;
const log = (m) => console.log(`  ✓ ${m}`);

(async () => {
  fs.mkdirSync(SHOTS, { recursive: true });
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1200, height: 800 }, deviceScaleFactor: 1 });
  const page = await ctx.newPage();

  const problems = [];
  // Real problems only: script errors and server errors (5xx). The browser also logs every 4xx response as a console
  // error, and this journey causes two on purpose (a rejected webhook, a wrong admin key), so those lines are ignored.
  page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) problems.push(`console: ${m.text()}`); });
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`));
  page.on('response', (r) => { if (r.status() >= 500) problems.push(`server error ${r.status()} on ${r.url()}`); });

  globalThis.__page = page;
  const shot = async (name) => page.screenshot({ path: path.join(SHOTS, `${String(++step).padStart(2, '0')}-${name}.png`), fullPage: true });
  const text = (loc) => loc.innerText();

  // ---- 1. guards ----
  await page.goto(`${BASE}/channels`);
  await page.waitForURL('**/login');
  log('anonymous visitor is sent to the sign-in page');
  await page.goto(`${BASE}/admin`);
  await page.waitForURL('**/admin/login');
  log('anonymous visitor cannot open the admin area');

  // ---- 2. user sign-in ----
  await page.goto(`${BASE}/login`);
  await page.getByLabel('Email address').fill(email);
  await shot('sign-in');
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.waitForURL('**/rules');
  await page.getByText('You have no rules yet').waitFor();
  await shot('rules-empty');
  log('signed in, empty rules screen shown');

  // ---- 3. channels ----
  await page.getByRole('link', { name: 'Channels' }).click();
  await page.getByRole('heading', { name: 'Channels' }).waitFor();
  await page.getByLabel('Type').selectOption('Slack');
  await page.getByLabel('Name').fill('Evil webhook');
  await page.getByLabel('Slack webhook URL').fill('https://169.254.169.254/services/a/b/c');
  await page.getByRole('button', { name: 'Add channel' }).click();
  await page.getByRole('alert').getByText(/hooks\.slack\.com/).waitFor();
  await shot('channel-rejected');
  log('a webhook pointing at an internal address is refused with a clear message');

  await page.getByLabel('Type').selectOption('Email');
  await page.getByLabel('Name').fill('My inbox');
  await page.getByLabel('Email address').fill(`inbox-${stamp}@example.com`);
  await page.getByRole('button', { name: 'Add channel' }).click();
  await page.getByRole('cell', { name: 'My inbox', exact: true }).waitFor();
  const masked = await text(page.locator('tbody tr').filter({ hasText: 'My inbox' }));
  assert.ok(masked.includes('i***@example.com'), `masked address expected, got: ${masked}`);
  assert.ok(!masked.includes(`inbox-${stamp}`), 'the real address must not be shown');
  log('email channel added and shown masked');

  // ---- the third channel: a dedicated form (it also worked before this form existed, through the generic JSON box) ----
  await page.getByLabel('Type').selectOption('Webhook');
  await page.getByLabel('Webhook URL').waitFor();
  assert.equal(await page.getByLabel('Signing secret').getAttribute('type'), 'password', 'the secret is typed into a masked field');
  await page.getByLabel('Name').fill('Internal hook');
  await page.getByLabel('Webhook URL').fill('https://169.254.169.254/latest/meta-data/');
  await page.getByLabel('Signing secret').fill('a-long-enough-secret-123');
  await page.getByRole('button', { name: 'Add channel' }).click();
  await page.getByRole('alert').getByText(/private or internal/).waitFor();
  assert.equal(await page.getByLabel('Webhook URL').inputValue(), 'https://169.254.169.254/latest/meta-data/', 'what was typed is kept so it can be corrected');
  await shot('webhook-internal-refused');
  log('a webhook aimed at the cloud metadata address is refused, and the form keeps what was typed');

  await page.getByLabel('Name').fill('Team hook');
  await page.getByLabel('Webhook URL').fill('https://hooks.example.com/in/TOKEN-ABC');
  await page.getByRole('button', { name: 'Add channel' }).click();
  const hook = page.locator('tbody tr').filter({ hasText: 'Team hook' });
  await hook.waitFor();
  const hookText = await text(hook);
  assert.ok(hookText.includes('https://hooks.example.com/… (signed)'), `masked webhook expected, got: ${hookText}`);
  assert.ok(!hookText.includes('TOKEN-ABC') && !hookText.includes('a-long-enough-secret'), 'neither the path token nor the secret may be shown');
  assert.equal(await page.getByLabel('Signing secret').inputValue(), '', 'the secret is gone from the form after saving');
  await shot('webhook-added');
  log('the webhook is added, shown masked, and the secret does not linger in the form');

  // a second channel the mail server will refuse permanently (for the failure path)
  await page.getByLabel('Type').selectOption('Email');
  await page.getByLabel('Name').fill('Bounces');
  await page.getByLabel('Email address').fill('reject@example.com');
  await page.getByRole('button', { name: 'Add channel' }).click();
  await page.getByRole('cell', { name: 'Bounces', exact: true }).waitFor();

  await page.locator('tbody tr').filter({ hasText: 'My inbox' }).getByRole('button', { name: 'Send test' }).click();
  await page.locator('tbody tr').filter({ hasText: 'My inbox' }).getByText('Works').waitFor();
  await shot('channels');
  log('"Send test" delivers a real message and shows the result');

  // ---- 4. rule from a preset, with preview ----
  await page.goto(`${BASE}/rules/new`);
  await page.getByLabel('Start from an example').selectOption({ label: 'Major earthquakes — Earthquakes of magnitude 6 or more anywhere in the world.' });
  assert.equal(await page.getByLabel('Magnitude at least').inputValue(), '6');
  assert.equal(await page.getByLabel('Watch for').inputValue(), 'Disasters');
  await page.getByLabel('Name').fill('Big quakes');
  await page.getByLabel('Place contains').fill(`Japan ${stamp}`);
  await page.getByLabel(/My inbox/).check();
  await page.getByRole('button', { name: 'Preview matches' }).click();
  await page.getByRole('heading', { name: 'Preview' }).waitFor();
  await shot('rule-preview');
  log('preset fills the form, preview answers');

  await page.getByRole('button', { name: 'Create rule' }).click();
  await page.waitForURL('**/rules');
  await page.getByRole('cell', { name: 'Big quakes', exact: true }).waitFor();

  // rule for the bouncing channel
  await page.goto(`${BASE}/rules/new`);
  await page.getByLabel('Name').fill('Quakes to a bad address');
  await page.getByLabel('Watch for').selectOption('Disasters');
  await page.getByLabel('Place contains').fill(`Chile ${stamp}`);
  await page.getByLabel(/Bounces/).check();
  await page.getByRole('button', { name: 'Create rule' }).click();
  await page.waitForURL('**/rules');
  await shot('rules');
  log('two rules created');

  // validation shown before a round trip
  await page.goto(`${BASE}/rules/new`);
  await page.getByRole('button', { name: 'Create rule' }).click();
  await page.getByRole('alert').getByText('Give the rule a name.').first().waitFor();
  log('an incomplete rule is explained, not silently rejected');

  // ---- 5. admin ----
  await page.getByRole('button', { name: 'Sign out' }).click();
  await page.waitForURL('**/login');
  await page.goto(`${BASE}/admin/login`);
  await page.getByLabel('Admin key').fill('wrong-key');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByRole('alert').waitFor();
  assert.equal(await page.getByLabel('Admin key').inputValue(), '', 'the wrong key is cleared');
  await shot('admin-wrong-key');
  await page.getByLabel('Admin key').fill('dev-admin-key');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL('**/admin');
  await page.getByRole('heading', { name: 'Overview' }).waitFor();
  log('wrong admin key refused, right one accepted');

  await page.goto(`${BASE}/rules`);              // an admin must not see user pages
  await page.waitForURL('**/admin');
  log('an administrator is kept out of user pages');

  // ---- 6. inject events ----
  await page.getByRole('link', { name: 'Events' }).click();
  const inject = async (title, magnitude, location) => {
    await page.getByLabel('Title').fill(title);
    await page.getByLabel('Magnitude').fill(String(magnitude));
    await page.getByLabel('Location').fill(location);
    await page.getByLabel('Event type').fill('earthquake');
    await page.getByRole('button', { name: 'Create event' }).click();
    // The confirmation names the event, so we never read the message left over from the previous one.
    const done = page.getByRole('status').filter({ hasText: title });
    await done.waitFor();
    return text(done);
  };
  const a = await inject(`M 6.8 - Sendai, Japan ${stamp}`, 6.8, `Sendai, Japan ${stamp}`);
  assert.match(a, /1 alert\(s\) queued/);
  const b = await inject(`M 5.2 - Tokyo, Japan ${stamp}`, 5.2, `Tokyo, Japan ${stamp}`);
  assert.match(b, /0 alert\(s\) queued/);
  const c = await inject(`M 6.9 - Santiago, Chile ${stamp}`, 6.9, `Santiago, Chile ${stamp}`);
  assert.match(c, /1 alert\(s\) queued/);
  await shot('events-injected');
  log('only matching events create alerts (strong Japan: yes, weak Japan: no, strong Chile: yes)');

  // ---- 7. deliveries ----
  await page.getByRole('link', { name: 'Deliveries' }).click();
  const row = (t) => page.locator('tbody > tr').filter({ hasText: t }).first();
  await row(`Sendai, Japan ${stamp}`).locator('.badge', { hasText: 'Sent' }).waitFor({ timeout: 20000 });
  await row(`Santiago, Chile ${stamp}`).locator('.badge', { hasText: 'Failed' }).waitFor({ timeout: 20000 });
  await row(`Sendai, Japan ${stamp}`).getByRole('button', { name: /Attempts/ }).click();
  await shot('deliveries');
  log('the Japan alert was Sent, the one to the refusing address Failed');

  await row(`Santiago, Chile ${stamp}`).getByRole('button', { name: /Attempts/ }).click();
  const attempts = page.locator('tbody tbody').last();          // the nested attempts table of the opened row
  await attempts.getByText('PermanentFailure').waitFor();
  assert.match(await text(attempts), /SMTP server rejected the message \(550\)/);
  assert.ok(!/https?:\/\//.test(await text(attempts)), 'attempt messages contain no URLs');
  await row(`Santiago, Chile ${stamp}`).getByRole('button', { name: 'Retry' }).click();
  await page.getByRole('status').filter({ hasText: 'Queued for another attempt' }).waitFor();
  log('retry is available for failed deliveries and confirms');

  // email really arrived at the SMTP server
  const mails = fs.existsSync(SMTP_OUT) ? fs.readdirSync(SMTP_OUT).map((f) => fs.readFileSync(path.join(SMTP_OUT, f), 'utf8')) : [];
  assert.ok(mails.some((m) => m.includes(`Sendai, Japan ${stamp}`)), 'the alert email reached the mail server');
  log('the alert email reached the real SMTP server');

  // ---- 8. switch a channel type off ----
  await page.getByRole('link', { name: 'Channel types' }).click();
  await page.locator('tbody tr').filter({ hasText: 'Email' }).getByRole('button', { name: 'Switch off' }).click();
  await page.getByRole('status').filter({ hasText: 'Email is now off' }).waitFor();
  await shot('channel-types-off');
  await page.getByRole('link', { name: 'Events' }).click();
  const off = await inject(`M 7.1 - Sendai, Japan (email off) ${stamp}`, 7.1, `Sendai, Japan ${stamp}`);
  assert.match(off, /0 alert\(s\) queued, 1 suppressed/);
  await page.getByRole('link', { name: 'Channel types' }).click();
  await page.locator('tbody tr').filter({ hasText: 'Email' }).getByRole('button', { name: 'Switch on' }).click();
  await page.getByRole('status').filter({ hasText: 'Email is now on' }).waitFor();
  log('switching Email off suppresses new alerts; switching it on again works');

  // ---- 9. sources and overview ----
  await page.getByRole('link', { name: 'Sources' }).click();
  await page.locator('tbody tr').filter({ hasText: 'usgs-earthquakes' }).locator('.badge').waitFor();
  await shot('sources');
  await page.getByRole('link', { name: 'Overview' }).click();
  await page.getByRole('heading', { name: 'Recent failures' }).waitFor();
  await shot('admin-overview');
  log('sources and overview render');

  // ---- 10. what the user sees ----
  await page.getByRole('button', { name: 'Sign out' }).click();
  await page.waitForURL('**/admin/login');
  await page.goto(`${BASE}/login`);
  await page.getByLabel('Email address').fill(email.toUpperCase());
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.waitForURL('**/rules');
  await page.getByRole('link', { name: 'History' }).click();
  await page.getByRole('cell', { name: new RegExp(`Sendai, Japan ${stamp}`) }).first().waitFor();
  const history = await text(page.locator('tbody').first());
  assert.match(history, /Sent/);
  assert.match(history, /Suppressed/);
  assert.match(history, /switched off by an administrator/);
  await shot('history');
  log('the user sees sent and suppressed alerts, with the reason for the suppressed one');

  // session survives a reload, ends on sign-out
  await page.reload();
  await page.getByRole('heading', { name: 'History' }).waitFor();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await page.waitForURL('**/login');
  await page.goBack();
  await page.waitForURL('**/login');
  log('session survives a reload and is gone after sign-out (back button does not bring it back)');

  // ---- small screen ----
  const phone = await browser.newContext({ viewport: { width: 375, height: 700 }, deviceScaleFactor: 2 });
  const p2 = await phone.newPage();
  await p2.goto(`${BASE}/login`);
  const overflow = await p2.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  assert.ok(overflow <= 0, `no horizontal scrolling on a phone (overflow ${overflow}px)`);
  await p2.screenshot({ path: path.join(SHOTS, `${String(++step).padStart(2, '0')}-phone-sign-in.png`) });
  log('sign-in fits a phone screen');

  await browser.close();
  assert.deepEqual(problems, [], `browser errors:\n${problems.join('\n')}`);
  log('no script errors and no server errors in the whole journey');
  console.log('\nJOURNEY PASSED');
})().catch(async (e) => {
  console.error('\nJOURNEY FAILED\n', e);
  try { await globalThis.__page?.screenshot({ path: path.join(SHOTS, 'failure.png'), fullPage: true }); console.error('screenshot: evidence/e2e/failure.png'); } catch { /* best effort */ }
  process.exit(1);
});
