// Shared helpers of the end-to-end runs: the browser, the Kazo Vector windows and what runs in
// their iframes (VectorCraft's page).
import { chromium } from 'playwright';
import { boot } from './host.mjs';

// VectorCraft's first start downloads about 70 MB of .wasm from the local host and compiles it.
export const START_TIMEOUT_MS = 120_000;

// A software WebGPU adapter (SwiftShader), so both of VectorCraft's renderers can be tried. With
// `webGpu: false` the browser has no WebGPU adapter, and every VectorCraft falls back to WebGL 2:
// a window started by the OS (xdg-open, a double click) cannot be given --webgl, and a software
// WebGPU device next to a WebGL context makes headless Chromium take both away (work item 3).
export const launchBrowser = ({ webGpu = true } = {}) => chromium.launch({
    args: webGpu
        ? ['--enable-unsafe-webgpu', '--enable-features=Vulkan', '--use-vulkan=swiftshader', '--use-webgpu-adapter=swiftshader', '--use-angle=vulkan']
        : ['--use-angle=swiftshader', '--disable-features=WebGPU'],
});

/** A fresh page with the OS booted; `errors` collects the page's uncaught errors. */
export async function bootPage(browser, baseUrl) {
    const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    // VECTOR_E2E_CONSOLE=1 echoes the page's console errors (an unhandled .NET exception shows there).
    if (process.env.VECTOR_E2E_CONSOLE) {
        page.on('console', message => message.type() === 'error' && console.error(`[console] ${message.text()}`));
    }
    await boot(page, baseUrl);
    return { page, errors };
}

/** Kazo Vector's OS windows, in opening order. */
export const vectorWindows = page => page.locator('article[data-window-id]', { has: page.locator('.vector-window') });

/** The iframe element of the window at `index` (opening order). */
export const iframeAt = (page, index) => vectorWindows(page).nth(index).locator('iframe.craft-frame-iframe');

/** The VectorCraft frame of the window at `index`, once the module put the iframe there. */
export async function frameAt(page, index) {
    const iframe = iframeAt(page, index);
    await iframe.waitFor({ state: 'attached', timeout: START_TIMEOUT_MS });
    return (await iframe.elementHandle()).contentFrame();
}

/**
 * Waits until VectorCraft runs in a frame: its start code removes #vectorcraft_loading once eframe
 * started on the canvas (apps/vectorcraft-web/src/web.rs), and the canvas then has a size.
 */
export async function waitForVectorCraft(frame) {
    await frame.waitForFunction(() => {
        const canvas = document.getElementById('vectorcraft_canvas');
        return document.getElementById('vectorcraft_loading') === null && canvas !== null && canvas.width > 0;
    }, undefined, { timeout: START_TIMEOUT_MS });
}

/**
 * Whether VectorCraft gave up its graphics: after three lost renderers it removes its canvas and
 * writes its message in #vectorcraft_loading (apps/vectorcraft-web/src/web.rs, give_up).
 */
export const graphicsFailed = frame => frame.evaluate(() => document.getElementById('vectorcraft_canvas') === null);

/** The renderer VectorCraft's canvas uses: `webgpu`, `webgl2` or null. */
export const rendererOf = frame => frame.evaluate(() => {
    const canvas = document.getElementById('vectorcraft_canvas');
    if (canvas.getContext('webgpu')) {
        return 'webgpu';
    }
    return canvas.getContext('webgl2') ? 'webgl2' : null;
});

// ---- Opening, saving and closing (work item 3) ----

/** How long one step waits: a dialog, a title, a save. */
export const STEP_TIMEOUT_MS = 30_000;

/** The home folder of the profile `boot` creates. */
export const HOME = '/home/neo';

/** Saves a screenshot of the page when VECTOR_E2E_SHOTS names a folder. */
export async function shot(page, name) {
    if (process.env.VECTOR_E2E_SHOTS) {
        await page.screenshot({ path: `${process.env.VECTOR_E2E_SHOTS}/${name}.png` });
    }
}

/** One OS window by its id. */
export const windowById = (page, id) => page.locator(`article[data-window-id="${id}"]`);

/** Waits for `count` Kazo Vector windows to show VectorCraft; returns the id of the newest one. */
export async function waitForWindow(page, count, known = []) {
    try {
        await page.locator('.vector-frame.running').nth(count - 1).waitFor({ timeout: START_TIMEOUT_MS });
    } catch (error) {
        await shot(page, `window-${count}`);
        throw error;
    }
    const ids = await vectorWindows(page).evaluateAll(nodes => nodes.map(node => node.getAttribute('data-window-id')));
    if (ids.length !== count) {
        throw new Error(`${ids.length} Kazo Vector windows instead of ${count}`);
    }
    return ids.find(id => !known.includes(id));
}

/** The VectorCraft frame of a window, by the window's id. */
export async function frameOf(page, id) {
    const handle = await windowById(page, id).locator('iframe.craft-frame-iframe').elementHandle();
    return handle.contentFrame();
}

/** Runs a method of VectorCraft's page API (window.vectorcraft) in a frame. */
export const request = (frame, method, params = {}) =>
    frame.evaluate(([name, args]) => window.vectorcraft.request(name, args), [method, params]);

/** Runs a VectorCraft engine command, as its menus do. */
export const execute = (frame, command, params = {}) => request(frame, 'engine.execute', { command, params });

/** Runs a VectorCraft menu item (File › Save…), which opens what the menu opens. */
export const menu = (frame, id) => request(frame, 'ui.menu.invoke', { id });

/** VectorCraft's open drawings (ui.inspect's documents: { id, title, path, dirty, active }). */
export async function documentsOf(frame) {
    const answer = await request(frame, 'ui.inspect');
    return answer?.documents ?? answer?.result?.documents ?? [];
}

/** Waits until one of the frame's drawings matches `predicate`. */
export async function waitForDocument(frame, predicate, description) {
    const deadline = Date.now() + STEP_TIMEOUT_MS;
    let last = [];
    while (Date.now() < deadline) {
        last = await documentsOf(frame).catch(error => [String(error)]);
        if (last.some(doc => typeof doc === 'object' && predicate(doc))) {
            return;
        }
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw new Error(`no drawing ${description}: ${JSON.stringify(last)}`);
}

/** Draws a rectangle in the drawing in front, so that it has unsaved changes. */
export async function drawRectangle(frame) {
    await execute(frame, 'shape.rectangle', { x: 20 + Math.round(Math.random() * 100), y: 20, width: 40, height: 30 });
}

/** Creates a drawing and draws in it; returns its tab's title. */
export async function newModifiedDrawing(frame) {
    await execute(frame, 'file.new', { width: 300, height: 200 });
    await drawRectangle(frame);
    await waitForDocument(frame, doc => doc.dirty && doc.active, 'modified after file.new');
    return (await documentsOf(frame)).find(doc => doc.active).title;
}

/** Answers the OS save dialog with a file name (in the folder it opens in). */
export async function answerSaveDialog(page, title, fileName) {
    const dialog = page.locator('section.file-dialog', { hasText: title });
    await dialog.waitFor({ state: 'visible', timeout: STEP_TIMEOUT_MS });
    await dialog.getByLabel('File name').fill(fileName);
    await dialog.getByRole('button', { name: 'Save' }).click();
    await dialog.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
}

/** Waits for a window's title bar to read `text`; on failure, says what it reads and the window's notice. */
export async function expectTitle(page, id, text) {
    try {
        await page.waitForFunction(
            ([windowId, wanted]) => document.querySelector(`article[data-window-id="${windowId}"] header.window-chrome h2`)?.textContent?.trim() === wanted,
            [id, text],
            { timeout: STEP_TIMEOUT_MS });
    } catch (error) {
        const actual = await windowById(page, id).locator('header.window-chrome h2').first().textContent().catch(() => null);
        const notice = await windowById(page, id).locator('.vector-notice').textContent().catch(() => null);
        await shot(page, `title-${id}`);
        throw new Error(`title "${actual}" instead of "${text}"; notice: ${notice}`, { cause: error });
    }
}
