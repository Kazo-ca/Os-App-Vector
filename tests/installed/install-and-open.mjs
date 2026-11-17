// Kazo Vector as the Kazo OS Store installs it (work items 2 and 4 of doc/en/plan.md, "Done when"):
// `npm run test:installed -- [--store] <Kazo OS wwwroot> [packages] [screenshots folder]`.
//
// --store: installs from the real Kazo OS Store (the published catalog of Os-AppMarketPlace, read
//   over the network) instead of `kazopkg install` from [packages]: the check after a release.
//
// <Kazo OS wwwroot>: a published Kazo OS, for example the kazoos-wasm-<version>.zip of a Kazo OS
//   release, unzipped (the trimmed OS users run; 3.3.4 is the minimum).
// [packages]: the folder `dotnet build src/Kazo.Vector -c Release -t:KazoOsPack` writes (default
//   src/Kazo.Vector/bin/kazoos-packages), the content of the kazoos-packages branch.
// [screenshots folder]: when given, the run also writes the Store's screenshots there (1.png …).
//
// The run serves the OS as a plain static host, answers the GitHub addresses of this repository's
// packages from [packages], installs Kazo Vector with `sudo kazopkg install` (its API check against
// the trimmed runtime included), starts it from the launcher and a second window next to it, and
// prints how long each took to show VectorCraft and how much the renderer's memory grew. In the
// first window a new drawing is saved through VectorCraft's save options and the OS save dialog.
// Then VectorCraft's own example poster (examples/dusk-poster.svg of the external/vectorcraft
// submodule, MIT OR Apache-2.0) is uploaded through the File Explorer, opened by a double-click,
// changed and saved in place, and the licenses are checked. Not part of CI: it needs WebGL 2 in the
// browser and several minutes.
import { execFileSync } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdir, readFile, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { chromium } from 'playwright';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const fromStore = process.argv.includes('--store');
const [wwwrootArgument, packagesArgument, shotsArgument] = process.argv.slice(2).filter(argument => argument !== '--store');
if (!wwwrootArgument) {
    console.error('usage: npm run test:installed -- [--store] <Kazo OS wwwroot> [packages folder] [screenshots folder]');
    process.exit(2);
}
const wwwroot = path.resolve(wwwrootArgument);
const packages = path.resolve(packagesArgument ?? path.join(root, 'src', 'Kazo.Vector', 'bin', 'kazoos-packages'));
const shots = shotsArgument && path.resolve(shotsArgument);
const POSTER = path.join(root, 'external', 'vectorcraft', 'examples', 'dusk-poster.svg');

const REPOSITORY = 'Kazo-ca/Os-App-Vector';
const APP = 'Kazo Vector';
const LOGIN = 'neo';
const PASSWORD = 'matrix123';
const HOME = `/home/${LOGIN}`;
const STEP_MS = 120_000;
// VectorCraft's first start downloads about 70 MB of .wasm and compiles it.
const START_MS = 240_000;
const TYPES = {
    '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
    '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png',
    '.webmanifest': 'application/manifest+json', '.dat': 'application/octet-stream', '.blat': 'application/octet-stream',
};
// WebGL 2 drawn on the CPU (SwiftShader), no WebGPU: a window the OS starts on a file cannot be
// given --webgl, and a software WebGPU device next to a WebGL context makes headless Chromium take
// both away (work item 3, tests/e2e/common.mjs).
const browserOptions = { args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--disable-features=WebGPU'] };

const log = message => console.log(`[installed] ${message}`);

// A plain static server, as a release host is: no COOP/COEP, no cache.
const server = createServer(async (request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://host').pathname);
    let file = path.join(wwwroot, relative);
    if (!file.startsWith(wwwroot)) {
        response.writeHead(403).end();
        return;
    }
    try {
        if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
        const body = await readFile(file);
        response.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        response.end(body);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const baseUrl = `http://127.0.0.1:${server.address().port}/`;

const waitForText = async (locator, predicate, what) => {
    const deadline = Date.now() + STEP_MS;
    while (Date.now() < deadline) {
        const text = (await locator.innerText()).replace(/ /g, ' ');
        if (predicate(text)) return text;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`timed out waiting for ${what}`);
};

const vectorWindows = page => page.locator('article[data-window-id]', { has: page.locator('.vector-window') });
const windowById = (page, id) => page.locator(`article[data-window-id="${id}"]`);

/** Waits for a new Kazo Vector window showing VectorCraft; returns its id and how long it took. */
async function waitForNewWindow(page, known, timeout = START_MS) {
    const started = Date.now();
    const deadline = started + timeout;
    while (Date.now() < deadline) {
        const ids = await vectorWindows(page).evaluateAll(nodes => nodes
            .filter(node => node.querySelector('.vector-frame.running'))
            .map(node => node.getAttribute('data-window-id')));
        const id = ids.find(candidate => !known.includes(candidate));
        if (id) return { id, ms: Date.now() - started };
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    const failure = await vectorWindows(page).locator('.vector-screen.failed').allInnerTexts().catch(() => []);
    throw new Error(`no new Kazo Vector window showed VectorCraft ${failure.join(' | ')}`);
}

async function frameOf(page, id) {
    const handle = await windowById(page, id).locator('iframe.craft-frame-iframe').elementHandle();
    return handle.contentFrame();
}

async function shoot(page, name) {
    if (!shots) return;
    await mkdir(shots, { recursive: true });
    // Let VectorCraft paint its last frame.
    await page.waitForTimeout(1500);
    await page.screenshot({ path: path.join(shots, name) });
    log(`screenshot ${name}`);
}

/** Resident memory of Chromium's renderer processes, in MiB (Linux `ps`; 0 elsewhere). */
const rendererMiB = () => {
    try {
        const lines = execFileSync('ps', ['-eo', 'rss=,args='], { encoding: 'utf8' }).split('\n');
        const kib = lines.filter(line => line.includes('--type=renderer')).reduce((sum, line) => sum + Number(line.trim().split(/\s+/)[0] || 0), 0);
        return kib / 1024;
    } catch {
        return 0;
    }
};

async function openApp(page, name) {
    await page.getByRole('button', { name: 'App launcher' }).click({ timeout: STEP_MS });
    await page.getByRole('combobox', { name: 'Search applications' }).fill(name);
    await page.getByRole('button', { name: `Launch ${name}`, exact: true }).click();
}

/** Closes every window whose content shows `text` (the File Explorer). */
async function closeWindowsShowing(page, text) {
    for (const close of await page.locator('article[data-window-id]', { hasText: text }).locator('header.window-chrome .chrome-button-close').all()) {
        await close.dispatchEvent('click');
    }
}

/** `sudo kazopkg install` in the Terminal, as an administrator. */
async function installWithKazopkg(page) {
    await openApp(page, 'Terminal');
    const rows = page.locator('[data-terminal-panel] .xterm-rows').first();
    await waitForText(rows, text => text.includes('$'), 'the shell prompt');
    await page.locator('[data-terminal-panel] .xterm-screen').first().click();
    await page.keyboard.type(`sudo kazopkg install ${REPOSITORY}`);
    await page.keyboard.press('Enter');
    const prompt = page.locator('.credential-prompt-dialog, .aaa-window').first();
    await prompt.waitFor({ state: 'visible', timeout: STEP_MS });
    const login = prompt.locator('input[type=text]');
    if (await login.count() > 0 && !(await login.inputValue())) await login.fill(LOGIN);
    await prompt.locator('input[type=password]').fill(PASSWORD);
    await prompt.locator('input[type=password]').press('Enter');
    const output = await waitForText(rows, text => text.includes('available now') || /does not have|refused|error/i.test(text), 'the end of the installation');
    if (!output.includes('available now')) throw new Error(`kazopkg did not install ${APP}:\n${output}`);
    log(`kazopkg installed ${APP}.`);
    await page.locator('[data-terminal-panel]').first().locator('xpath=ancestor::article[1]').locator('header.window-chrome .chrome-button-close').click();
}

/** The Kazo OS Store: search Kazo Vector in the published catalog, "Install for me". */
async function installFromStore(page) {
    await openApp(page, 'Kazo OS Store');
    const store = page.locator('.store-root').first();
    await store.locator('input.store-search').fill(APP);
    // Kazo OS 3.3.4 Store: a card (not a button) whose title opens the app's page.
    const card = store.locator('.store-card', { hasText: APP }).first();
    await card.waitFor({ timeout: STEP_MS });
    await card.getByText(APP, { exact: true }).first().click();
    await store.getByRole('button', { name: 'Install for me', exact: true }).click({ timeout: STEP_MS });
    // A first install from the catalog may ask an administrator.
    const prompt = page.locator('.credential-prompt-dialog, .aaa-window').first();
    const opened = store.getByRole('button', { name: 'Open', exact: true });
    await Promise.race([
        prompt.waitFor({ state: 'visible', timeout: STEP_MS }).then(async () => {
            const login = prompt.locator('input[type=text]');
            if (await login.count() > 0 && !(await login.inputValue())) await login.fill(LOGIN);
            await prompt.locator('input[type=password]').fill(PASSWORD);
            await prompt.locator('input[type=password]').press('Enter');
        }),
        opened.waitFor({ timeout: STEP_MS }),
    ]).catch(() => {});
    try {
        await opened.waitFor({ timeout: STEP_MS * 2 });
    } catch (error) {
        const banner = await store.locator('.store-banner').allInnerTexts().catch(() => []);
        throw new Error(`the Store did not install ${APP}: ${banner.join(' | ')}`, { cause: error });
    }
    log(`The Kazo OS Store installed ${APP}.`);
    await store.locator('xpath=ancestor::article[1]').locator('header.window-chrome .chrome-button-close').click();
}

/** VectorCraft's page API (window.vectorcraft in the iframe), as its menus and tools run it. */
const request = (frame, method, params = {}) => frame.evaluate(([name, args]) => window.vectorcraft.request(name, args), [method, params]);
const execute = (frame, command, params = {}) => request(frame, 'engine.execute', { command, params });

/** VectorCraft's open drawings (ui.inspect documents: { id, title, path, dirty, active }). */
async function documentsOf(frame) {
    const answer = await request(frame, 'ui.inspect');
    return answer?.documents ?? answer?.result?.documents ?? [];
}

async function waitForDocuments(frame, check, what) {
    const deadline = Date.now() + STEP_MS;
    let last = [];
    while (Date.now() < deadline) {
        last = await documentsOf(frame).catch(error => [String(error)]);
        if (check(last)) return last;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`VectorCraft's drawings are not ${what}: ${JSON.stringify(last)}`);
}

async function expectTitle(page, id, text) {
    try {
        await page.waitForFunction(
            ([windowId, wanted]) => document.querySelector(`article[data-window-id="${windowId}"] header.window-chrome h2`)?.textContent?.trim() === wanted,
            [id, text],
            { timeout: STEP_MS });
    } catch (error) {
        const actual = await windowById(page, id).locator('header.window-chrome h2').first().textContent().catch(() => null);
        const notice = await windowById(page, id).locator('.vector-notice').textContent().catch(() => null);
        throw new Error(`title "${actual}" instead of "${text}"; notice: ${notice}`, { cause: error });
    }
}

/** Answers the OS save dialog titled `title` with a file name. */
async function answerSaveDialog(page, title, fileName) {
    const dialog = page.locator('section.file-dialog', { hasText: title });
    await dialog.waitFor({ state: 'visible', timeout: STEP_MS });
    await dialog.getByLabel('File name').fill(fileName);
    await dialog.getByRole('button', { name: 'Save' }).click();
    await dialog.waitFor({ state: 'hidden', timeout: STEP_MS });
}

/** Closes a Kazo Vector window; "No" when it asks to save. */
async function closeWithoutSaving(page, id) {
    await windowById(page, id).locator('header.window-chrome .chrome-button-close').click();
    const question = page.locator('.message-box-dialog', { hasText: 'Save the changes' });
    await Promise.race([
        question.waitFor({ timeout: 10_000 }).then(() => question.getByRole('button', { name: 'No' }).click()),
        windowById(page, id).waitFor({ state: 'detached', timeout: 10_000 }),
    ]).catch(() => {});
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_MS });
}

/** egui paints on input: move the pointer over the canvas so the last frame is drawn. */
async function repaint(page, id) {
    const box = await windowById(page, id).locator('iframe.craft-frame-iframe').boundingBox();
    for (let step = 0; step < 6; step++) {
        await page.mouse.move(box.x + box.width - 80 - step * 7, box.y + box.height - 60 - step * 5);
        await page.waitForTimeout(300);
    }
}

// The Store reads the catalog on GitHub: go through the machine's proxy when there is one.
const proxy = process.env.HTTPS_PROXY ?? process.env.https_proxy;
if (fromStore && proxy) browserOptions.proxy = { server: proxy, bypass: '127.0.0.1,localhost' };

const browser = await chromium.launch(browserOptions);
const errors = [];
let page;
try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, ignoreHTTPSErrors: fromStore && Boolean(proxy) });
    // raw.githubusercontent.com, as kazopkg reads it (ADR 0182 of Kazo OS), answered locally.
    if (!fromStore) await context.route(new RegExp(`^https://raw\\.githubusercontent\\.com/${REPOSITORY}/kazoos-packages/`), async route => {
        const relative = decodeURIComponent(new URL(route.request().url()).pathname).split('/kazoos-packages/')[1];
        const file = path.join(packages, relative);
        const headers = { 'Access-Control-Allow-Origin': '*' };
        try {
            if (!file.startsWith(packages)) throw new Error('outside the packages folder');
            await route.fulfill({ status: 200, body: await readFile(file), headers: { ...headers, 'Content-Type': 'application/octet-stream' } });
            log(`served ${relative}`);
        } catch {
            await route.fulfill({ status: 404, headers });
        }
    });
    page = await context.newPage();
    page.on('pageerror', error => errors.push(String(error)));

    // First boot: the administrator is created.
    await page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: 'Create administrator' }).waitFor({ timeout: 180_000 });
    await page.getByLabel('Login name').fill(LOGIN);
    await page.getByLabel('Display name').fill('Neo');
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirm password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Create local profile' }).click();
    // The welcome wizard (Kazo OS ADR 0276, from 3.4.0) shows once per account: skip it when
    // this Kazo OS version has it, so the boot works on older and newer versions alike.
    const skip = page.locator('.welcome-skip');
    await skip.or(page.getByText('Applications', { exact: true })).first().waitFor({ timeout: STEP_MS });
    if (await skip.isVisible()) {
        await skip.click();
    }
    await page.getByText('Applications', { exact: true }).waitFor({ timeout: STEP_MS });

    if (fromStore) {
        await installFromStore(page);
    } else {
        await installWithKazopkg(page);
    }
    const baseline = rendererMiB();

    // Kazo Vector from the launcher: the first start downloads and compiles VectorCraft.
    await openApp(page, APP);
    const first = await waitForNewWindow(page, []);
    const frame = await frameOf(page, first.id);
    const afterFirst = rendererMiB();
    log(`First window: VectorCraft shown after ${(first.ms / 1000).toFixed(1)} s (download and compile), renderer memory +${(afterFirst - baseline).toFixed(0)} MiB.`);

    // A second window: its own VectorCraft, from the browser cache.
    await openApp(page, APP);
    const second = await waitForNewWindow(page, [first.id]);
    const afterSecond = rendererMiB();
    log(`Second window: VectorCraft shown after ${(second.ms / 1000).toFixed(1)} s, renderer memory +${(afterSecond - afterFirst).toFixed(0)} MiB.`);
    await closeWithoutSaving(page, second.id);

    // A new drawing, saved for the first time: VectorCraft's save options give the name and format
    // (run as file.save with the path they give), then the OS save dialog the folder (decision 8).
    await execute(frame, 'file.new', { width: 400, height: 300 });
    await execute(frame, 'shape.ellipse', { x: 120, y: 70, width: 160, height: 160 });
    await waitForDocuments(frame, docs => docs.some(doc => doc.active && doc.dirty), 'modified');
    await execute(frame, 'file.save', { path: 'badge.vectorcraft', options: {} });
    await answerSaveDialog(page, 'Save the drawing', 'badge.vectorcraft');
    await expectTitle(page, first.id, `badge.vectorcraft — ${APP}`);
    await waitForDocuments(frame, docs => docs.some(doc => doc.active && !doc.dirty && doc.path === 'badge.vectorcraft'), 'saved as badge.vectorcraft');
    log('A new drawing was saved as badge.vectorcraft through the OS save dialog.');
    await closeWithoutSaving(page, first.id);

    // VectorCraft's example poster, uploaded through the File Explorer.
    await openApp(page, 'File Explorer');
    const row = name => page.locator(`tr[data-item-path="${HOME}/${name}"]`).first();
    await row('badge.vectorcraft').waitFor({ timeout: STEP_MS });
    // An upload chosen before the Explorer is ready is lost (tests/e2e/measure-large.mjs).
    await page.waitForTimeout(3_000);
    await page.locator('#fileInput').setInputFiles(POSTER);
    await row('dusk-poster.svg').waitFor({ timeout: STEP_MS });

    // A double-click on the SVG: the installed app's file type opens it, in a window of its own.
    await row('dusk-poster.svg').dblclick({ position: { x: 40, y: 10 } });
    const opened = await waitForNewWindow(page, []);
    log(`Window on dusk-poster.svg: VectorCraft shown after ${(opened.ms / 1000).toFixed(1)} s (VectorCraft in the browser cache).`);
    await expectTitle(page, opened.id, `dusk-poster.svg — ${APP}`);
    const poster = await frameOf(page, opened.id);
    await waitForDocuments(poster, docs => docs.some(doc => doc.active && doc.path === 'dusk-poster.svg' && !doc.dirty), 'on dusk-poster.svg');
    log(`A double-click on dusk-poster.svg opened it in ${APP}.`);

    // A change saved in place: the window is bound to dusk-poster.svg, no dialog.
    await execute(poster, 'shape.rectangle', { x: 0, y: 0, width: 4, height: 4 });
    await expectTitle(page, opened.id, `*dusk-poster.svg — ${APP}`);
    await execute(poster, 'file.save');
    await expectTitle(page, opened.id, `dusk-poster.svg — ${APP}`);
    if (await page.locator('section.file-dialog').count() > 0) throw new Error('saving in place showed a dialog');
    await waitForDocuments(poster, docs => docs.some(doc => doc.active && !doc.dirty), 'clean after the save');
    log(`${APP} saved dusk-poster.svg in place.`);

    // The licenses came with the package.
    for (const file of ['legal/LICENSE.txt', 'legal/THIRD-PARTY-NOTICES.md', 'legal/THIRD-PARTY-NOTICES-fr.md', 'legal/vectorcraft/LICENSE-lucide.txt',
        'legal/vectorcraft/LICENSE-indic-hyphenation-MIT.txt', 'vectorcraft/NOTICE', 'vectorcraft/LICENSE-MIT', 'vectorcraft/LICENSE-APACHE',
        'vectorcraft/ASSETS.md', 'vectorcraft/OFL-Inter.txt']) {
        // Resolved from the page's own URL (host/index.html), not its <base href="../vectorcraft/">.
        const status = await poster.evaluate(async url => (await fetch(url)).status, new URL(`../${file}`, poster.url()).href);
        if (status !== 200) throw new Error(`${file} is not in the installed package (${status})`);
    }
    log('The licenses are installed with the app.');

    // Screenshot 3: a File Explorer in front of the window, dusk-poster.svg selected.
    await closeWindowsShowing(page, 'badge.vectorcraft');
    await openApp(page, 'File Explorer');
    await row('dusk-poster.svg').waitFor({ timeout: STEP_MS });
    await row('dusk-poster.svg').click({ position: { x: 40, y: 10 } });
    // The pointer off the list, so no tooltip covers it.
    await page.mouse.move(1300, 880);
    await shoot(page, '3.png');
    await closeWindowsShowing(page, 'badge.vectorcraft');

    // Screenshot 1: the poster, the window maximized and the artboard fitted.
    const chrome = windowById(page, opened.id).locator('header.window-chrome').first();
    await chrome.dblclick({ position: { x: 300, y: 12 } });
    await page.waitForTimeout(2000);
    await request(poster, 'ui.menu.invoke', { id: 'view.fitArtboard' }).catch(() => {});
    // Nothing selected: the rectangle the save added stays selected otherwise, with its task bar.
    await execute(poster, 'select.none');
    await repaint(page, opened.id);
    await shoot(page, '1.png');

    // Screenshot 2: everything selected, with its bounding box and the panels filled.
    await execute(poster, 'select.all');
    await repaint(page, opened.id);
    await shoot(page, '2.png');

    if (errors.length > 0) throw new Error(`page errors: ${errors.join(' | ')}`);
    log('PASS');
} catch (error) {
    console.error(`[installed] FAIL: ${error.message}`);
    await page?.screenshot({ path: 'installed-failure.png' }).catch(() => {});
    process.exitCode = 1;
} finally {
    await browser.close();
    server.close();
}
