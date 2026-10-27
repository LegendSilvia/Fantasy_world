// The web viewer's browser check (sub-project 11 W3): serve a published site with a world bundle beside it, then
//   node smoke.js http://127.0.0.1:8765/ [screenshot-dir]
// It opens the world, follows a realm, a person and a why, moves the year back, and fails on any page error.
const { chromium } = require('playwright');

(async () => {
  const [url, shots] = process.argv.slice(2);
  // Behind a TLS-inspecting proxy (a sandbox), the CDN's certificate is the proxy's: SMOKE_IGNORE_CERTS=1 accepts it.
  const browser = await chromium.launch({ args: process.env.SMOKE_IGNORE_CERTS === '1' ? ['--ignore-certificate-errors'] : [] });
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  const problems = [];
  page.on('pageerror', e => problems.push(e.message.split('\n')[0]));
  const fail = message => { console.error(`web smoke: ${message}`); process.exitCode = 1; };
  try {
    await page.goto(url, { waitUntil: 'load' });
    await page.waitForFunction(() => document.body.dataset.ready, null, { timeout: 120000 });
    if (await page.evaluate(() => document.body.dataset.ready) !== 'true') throw new Error(await page.textContent('#loading-text'));
    const realms = await page.locator('.label.realm').count();
    if (realms === 0) throw new Error('no realm is named');
    if (shots) await page.screenshot({ path: `${shots}/web-map.png` });
    await page.locator('.label.realm').first().click();
    await page.waitForFunction(() => location.hash.startsWith('#realm/') && !document.getElementById('panel').hidden);
    await page.click('#panel-body a[data-ref^="char#"]');
    await page.waitForFunction(() => location.hash.startsWith('#character/'));
    await page.click('#panel-body a[title="Why?"]');
    await page.waitForFunction(() => location.hash.startsWith('#why/'));
    if (shots) await page.screenshot({ path: `${shots}/web-why.png` });
    const year = Number(await page.getAttribute('#year', 'max'));
    await page.evaluate(y => { location.hash = `year/${y}`; }, Math.max(1, year - 10));
    await page.waitForFunction(y => document.getElementById('year-label').textContent === String(y), Math.max(1, year - 10));
    console.log(`web smoke: opened, ${realms} realms named, realm → person → why followed, year ${year - 10} shown.`);
  } catch (e) {
    fail(e.message);
  }
  if (problems.length) fail(`page errors: ${problems.join('; ')}`);
  await browser.close();
})();
