// Kazo Vector installed in a published Kazo OS (work item 2 of doc/en/plan.md):
// `npm run test:installed -- <Kazo OS wwwroot> [packages]`.
//
// <Kazo OS wwwroot>: a published Kazo OS, for example the kazoos-wasm-<version>.zip of a Kazo OS
//   release, unzipped (the trimmed OS users run; 3.3.4 is the minimum).
// [packages]: the folder `dotnet build src/Kazo.Vector -c Release -t:KazoOsPack` writes (default
//   src/Kazo.Vector/bin/kazoos-packages), the content of the kazoos-packages branch.
//
// The run serves the OS as a plain static host, answers the GitHub addresses of this repository's
// packages from [packages], installs Kazo Vector with `sudo kazopkg install` (its API check against
// the trimmed runtime included), opens two Kazo Vector windows from the launcher and waits until
// VectorCraft runs in each iframe. It prints how long each start took and the memory of the
// browser's renderer process with the OS alone, one window and two. Not part of CI: it needs
// WebGL 2 in the browser and several minutes. Work item 3 extends it to opening and saving.
import { execFileSync } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { chromium } from 'playwright';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const [wwwrootArgument, packagesArgument] = process.argv.slice(2);
if (!wwwrootArgument) {
    console.error('usage: npm run test:installed -- <Kazo OS wwwroot> [packages folder]');
    process.exit(2);
}
const wwwroot = path.resolve(wwwrootArgument);
const packages = path.resolve(packagesArgument ?? path.join(root, 'src', 'Kazo.Vector', 'bin', 'kazoos-packages'));

const REPOSITORY = 'Kazo-ca/Os-App-Vector';
const APP = 'Kazo Vector';
const LOGIN = 'neo';
const PASSWORD = 'matrix123';
const STEP_MS = 120_000;
// VectorCraft's first start downloads about 70 MB of .wasm and compiles it.
const START_MS = 240_000;
const TYPES = {
    '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
    '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png',
    '.webmanifest': 'application/manifest+json', '.dat': 'application/octet-stream', '.blat': 'application/octet-stream',
};
// WebGL 2 drawn on the CPU (SwiftShader): Chromium's software WebGPU adapter loses its device in
// headless runs.
// WebGL 2 by SwiftShader by default; RENDERER=webgpu adds a software WebGPU adapter, which VectorCraft
// then prefers (the measures of doc/en/plan.md give both).
const browserOptions = process.env.RENDERER === 'webgpu'
    ? { args: ['--enable-unsafe-webgpu', '--enable-features=Vulkan', '--use-vulkan=swiftshader', '--use-webgpu-adapter=swiftshader', '--use-angle=vulkan'] }
    : { args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] };

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
        const text = (await locator.innerText()).replace(/ /g, ' ');
        if (predicate(text)) return text;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`timed out waiting for ${what}`);
};

const vectorWindows = page => page.locator('article[data-window-id]', { has: page.locator('.vector-window') });

/** Waits until VectorCraft runs in the iframe of the window at `index`; returns how long it took. */
async function waitForVectorCraft(page, index) {
    const started = Date.now();
    const iframe = vectorWindows(page).nth(index).locator('iframe.craft-frame-iframe');
    await iframe.waitFor({ state: 'attached', timeout: START_MS });
    const frame = await (await iframe.elementHandle()).contentFrame();
    await frame.waitForFunction(() => {
        const canvas = document.getElementById('vectorcraft_canvas');
        return document.getElementById('vectorcraft_loading') === null && canvas !== null && canvas.width > 0;
    }, undefined, { timeout: START_MS });
    return Date.now() - started;
}

async function openApp(page, name) {
    await page.getByRole('button', { name: 'App launcher' }).click({ timeout: STEP_MS });
    await page.getByRole('combobox', { name: 'Search applications' }).fill(name);
    await page.getByRole('button', { name: `Launch ${name}`, exact: true }).click();
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

/** Resident memory of Chromium's renderer processes, in MiB (Linux `ps`). */
function rendererMemoryMiB() {
    const lines = execFileSync('ps', ['-eo', 'rss=,args='], { encoding: 'utf8' }).split('\n');
    const kib = lines.filter(line => line.includes('--type=renderer')).reduce((sum, line) => sum + Number(line.trim().split(/\s+/)[0] || 0), 0);
    return Math.round(kib / 1024);
}

const browser = await chromium.launch(browserOptions);
const errors = [];
try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    // raw.githubusercontent.com, as kazopkg reads it (ADR 0182 of Kazo OS), answered locally.
    await context.route(new RegExp(`^https://raw\\.githubusercontent\\.com/${REPOSITORY}/kazoos-packages/`), async route => {
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
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(String(error)));

    // First boot: the administrator is created.
    await page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: 'Create administrator' }).waitFor({ timeout: 180_000 });
    await page.getByLabel('Login name').fill(LOGIN);
    await page.getByLabel('Display name').fill('Neo');
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirm password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Create local profile' }).click();
    await page.getByText('Applications', { exact: true }).waitFor({ timeout: STEP_MS });

    await installWithKazopkg(page);
    const alone = rendererMemoryMiB();

    await openApp(page, APP);
    log(`window 1: VectorCraft ran in ${await waitForVectorCraft(page, 0)} ms`);
    const one = rendererMemoryMiB();
    await openApp(page, APP);
    log(`window 2: VectorCraft ran in ${await waitForVectorCraft(page, 1)} ms`);
    const two = rendererMemoryMiB();
    log(`renderer memory: ${alone} MiB OS alone, ${one} MiB with one window, ${two} MiB with two`);

    if (errors.length > 0) throw new Error(`page errors: ${errors.join(' | ')}`);
    log('OK');
} finally {
    await browser.close();
    server.close();
}
