// End-to-end runs of the Kazo Vector window inside the Kazo OS host (doc/en/plan.md, work item 2):
// `npm run test:e2e` after `dotnet build Kazo.Vector.slnx -c Debug`. Each test opens a fresh page.
//
// The window shows VectorCraft once VectorCraft says "ready" (vectorcraft:ready), which the host
// mode of our fork sends (fork work item F1, kazo/base).
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { openApp, runInTerminal, startHost } from './host.mjs';
import { bootPage, vectorWindows, frameAt, graphicsFailed, launchBrowser, rendererOf, waitForVectorCraft } from './common.mjs';

let host;
let browser;

before(async () => {
    host = await startHost();
    browser = await launchBrowser();
});

after(async () => {
    await browser?.close();
    host?.stop();
});

test('Kazo Vector opens from the Applications menu and VectorCraft starts in its iframe', { timeout: 300_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await openApp(page, 'Kazo Vector');
    const frame = await frameAt(page, 0);
    const query = new URL(frame.url()).searchParams;
    assert.equal(query.has('control'), false, 'VectorCraft always installs window.vectorcraft (decision 6)');
    assert.equal(query.has('webgl'), false);

    await waitForVectorCraft(frame);

    assert.deepEqual(errors, []);
    await page.close();
});

test('the --webgl launch argument starts VectorCraft on WebGL 2', { timeout: 300_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await runInTerminal(page, 'launch ca.kazo.vector --webgl');
    const frame = await frameAt(page, 0);
    assert.equal(new URL(frame.url()).searchParams.has('webgl'), true);
    await waitForVectorCraft(frame);
    assert.equal(await rendererOf(frame), 'webgl2');
    assert.equal(await graphicsFailed(frame), false, 'VectorCraft kept its graphics');

    assert.deepEqual(errors, []);
    await page.close();
});

test('two Kazo Vector windows run their own VectorCraft at the same time', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await runInTerminal(page, 'launch ca.kazo.vector --webgl');
    const first = await frameAt(page, 0);
    await waitForVectorCraft(first);
    await runInTerminal(page, 'launch ca.kazo.vector --webgl');
    const second = await frameAt(page, 1);
    await waitForVectorCraft(second);

    assert.equal(await vectorWindows(page).count(), 2);
    assert.notEqual(first, second);
    assert.equal(await graphicsFailed(first), false);
    assert.equal(await graphicsFailed(second), false);

    assert.deepEqual(errors, []);
    await page.close();
});

test('the window shows VectorCraft once it is ready', { timeout: 300_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);

    await openApp(page, 'Kazo Vector');
    await page.locator('.vector-frame.running').first().waitFor({ timeout: 120_000 });
    assert.equal(await page.locator('.vector-screen').count(), 0, 'no loading or error screen is left');

    assert.deepEqual(errors, []);
    await page.close();
});
