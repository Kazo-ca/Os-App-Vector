// End-to-end runs of opening, saving, exporting and closing (doc/en/plan.md, work item 3,
// decisions 7 to 10, 13), inside the Kazo OS host: `npm run test:e2e` after
// `dotnet build Kazo.Vector.slnx -c Debug`.
//
// VectorCraft is driven through its page API (window.vectorcraft in the iframe) to draw and to run
// its commands and menu items, as its menus and shortcuts do. A save that VectorCraft would first
// ask about in its own "Save options" dialog (name and format) is run as `file.save`/`file.saveAs`
// with the `path` that dialog gives. Everything else goes through the OS: its save and open
// dialogs, its message box, the window's title bar and close button, and the Terminal (`xdg-open`
// starts the app associated with the file type). The browser has no WebGPU (launchBrowser): every
// window, including those the OS starts on a file, runs VectorCraft on WebGL 2.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { runInTerminal, startHost } from './host.mjs';
import {
    HOME, STEP_TIMEOUT_MS, answerSaveDialog, bootPage, documentsOf, drawRectangle, execute, expectTitle, frameOf, launchBrowser, menu,
    newModifiedDrawing, request, shot, vectorWindows, waitForDocument, waitForWindow, windowById,
} from './common.mjs';

const SAVE_TITLE = 'Save the drawing';
const COPY_TITLE = 'Save a copy';
const OPEN_TITLE = 'Open drawings';

let host;
let browser;

before(async () => {
    host = await startHost();
    browser = await launchBrowser({ webGpu: false });
});

after(async () => {
    await browser?.close();
    host?.stop();
});

/** Opens Kazo Vector on WebGL 2 from the Terminal; returns its window id and VectorCraft's frame. */
async function launch(page, known = [], argument = '') {
    await runInTerminal(page, `launch ca.kazo.vector --webgl ${argument}`.trim());
    const id = await waitForWindow(page, known.length + 1, known);
    return { id, frame: await frameOf(page, id) };
}

/** Waits for the Terminal's output to show `text`. */
async function expectInTerminal(page, text) {
    await page.locator('.xterm-rows').last().getByText(text, { exact: false }).first().waitFor({ timeout: STEP_TIMEOUT_MS });
}

/**
 * Lets an OS file dialog finish watching the folder it just entered before it closes (a Kazo OS
 * file view throws when it is disposed while that watch starts). A person never picks that fast.
 */
const settleAfterNavigation = page => page.waitForTimeout(1_000);

/** A new drawing saved as `fileName` in the home folder: VectorCraft's save options, then the OS dialog. */
async function saveNewDrawing(page, id, frame, fileName) {
    await newModifiedDrawing(frame);
    await execute(frame, 'file.save', { path: fileName, options: {} });
    await answerSaveDialog(page, SAVE_TITLE, fileName);
    await expectTitle(page, id, `${fileName} — Kazo Vector`);
    await waitForDocument(frame, doc => doc.active && !doc.dirty, 'clean after the save');
}

test('a new drawing is saved where the user says, saved again in place, and reopens from the OS', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);

    const title = await newModifiedDrawing(frame);
    await expectTitle(page, id, `*${title} — Kazo Vector`);

    // A first save, whatever name VectorCraft's own dialog gives, asks the OS where to write it
    // (F1 case 2, saveAs true); the drawing is clean only once the file is written (H4).
    await execute(frame, 'file.save', { path: 'Untitled.vectorcraft', options: {} });
    await answerSaveDialog(page, SAVE_TITLE, 'poster.vectorcraft');
    await expectTitle(page, id, 'poster.vectorcraft — Kazo Vector');
    await waitForDocument(frame, doc => doc.active && !doc.dirty, 'clean after the save');

    // The next File › Save writes the same file, with no dialog.
    await drawRectangle(frame);
    await expectTitle(page, id, '*poster.vectorcraft — Kazo Vector');
    await menu(frame, 'file.save');
    await expectTitle(page, id, 'poster.vectorcraft — Kazo Vector');
    assert.equal(await page.locator('section.file-dialog').count(), 0);

    // `xdg-open` starts the app associated with .vectorcraft: a second window, on the file.
    await runInTerminal(page, `xdg-open ${HOME}/poster.vectorcraft`);
    const second = await waitForWindow(page, 2, [id]);
    await expectTitle(page, second, 'poster.vectorcraft — Kazo Vector');
    await waitForDocument(await frameOf(page, second), doc => doc.path === 'poster.vectorcraft', 'poster.vectorcraft');

    assert.deepEqual(errors, []);
    await page.close();
});

test('an SVG opened from the OS is rewritten in place; Save As and a copy go where the user says', { timeout: 500_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await newModifiedDrawing(frame);

    // Save a Copy as SVG is an export (ADR 0264): a copy where the user says, the drawing unbound.
    await execute(frame, 'file.saveCopy', { path: 'logo.svg', svg: {} });
    await answerSaveDialog(page, COPY_TITLE, 'logo.svg');
    await windowById(page, id).locator('.vector-notice', { hasText: 'Copy saved: logo.svg' }).waitFor({ timeout: STEP_TIMEOUT_MS });
    await waitForDocument(frame, doc => doc.active && doc.dirty, 'still modified after the copy');

    // The SVG opens in a new window (a double click: each file starts its own window), bound to it.
    await runInTerminal(page, `xdg-open ${HOME}/logo.svg`);
    const second = await waitForWindow(page, 2, [id]);
    const svg = await frameOf(page, second);
    await expectTitle(page, second, 'logo.svg — Kazo Vector');
    await waitForDocument(svg, doc => doc.path === 'logo.svg' && !doc.dirty, 'logo.svg');

    // F1 case 1: File › Save over the SVG writes it again, with no dialog.
    await drawRectangle(svg);
    await expectTitle(page, second, '*logo.svg — Kazo Vector');
    await menu(svg, 'file.save');
    await expectTitle(page, second, 'logo.svg — Kazo Vector');
    assert.equal(await page.locator('section.file-dialog').count(), 0);

    // F1 case 4: a format change (VectorCraft's Save As options, .vectorcraft) asks for the new file
    // and binds the drawing to it.
    await drawRectangle(svg);
    await execute(svg, 'file.saveAs', { path: 'logo.vectorcraft', options: {} });
    await answerSaveDialog(page, SAVE_TITLE, 'logo.vectorcraft');
    await expectTitle(page, second, 'logo.vectorcraft — Kazo Vector');
    await waitForDocument(svg, doc => doc.path === 'logo.vectorcraft' && !doc.dirty, 'bound to logo.vectorcraft');

    await runInTerminal(page, `ls ${HOME}`);
    await expectInTerminal(page, 'logo.vectorcraft');
    assert.match(await page.locator('.xterm-rows').last().innerText(), /logo\.svg/);

    assert.deepEqual(errors, []);
    await page.close();
});

test('closing with changes asks; Cancel keeps the window, Yes saves through VectorCraft before closing', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    const title = await newModifiedDrawing(frame);
    await expectTitle(page, id, `*${title} — Kazo Vector`);

    const close = windowById(page, id).locator('header.window-chrome .chrome-button-close');
    const question = page.locator('.message-box-dialog', { hasText: 'Save the changes' });

    // Cancel: nothing closes, nothing is lost.
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Cancel' }).click();
    await question.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
    assert.equal(await windowById(page, id).count(), 1);
    await waitForDocument(frame, doc => doc.dirty, 'still modified');

    // Yes: VectorCraft's File › Save asks its own name and format first (a drawing never saved),
    // then the OS dialog. Cancelling the OS dialog keeps the window and says so.
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Yes' }).click();
    await waitForDialog(frame);
    await request(frame, 'ui.dialog.confirm');
    const saveDialog = page.locator('section.file-dialog', { hasText: SAVE_TITLE });
    await saveDialog.waitFor({ timeout: STEP_TIMEOUT_MS });
    await saveDialog.getByRole('button', { name: 'Cancel' }).click();
    await windowById(page, id).locator('.vector-notice', { hasText: 'was not saved' }).waitFor({ timeout: STEP_TIMEOUT_MS });
    assert.equal(await windowById(page, id).count(), 1);
    await waitForDocument(frame, doc => doc.dirty, 'still modified after the cancelled save');

    // Yes, then a file name: the drawing is written, then the window closes.
    await close.click();
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'Yes' }).click();
    await waitForDialog(frame);
    await request(frame, 'ui.dialog.confirm');
    await answerSaveDialog(page, SAVE_TITLE, 'closing.vectorcraft');
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    await runInTerminal(page, `xdg-open ${HOME}/closing.vectorcraft`);
    const reopened = await waitForWindow(page, 1);
    await expectTitle(page, reopened, 'closing.vectorcraft — Kazo Vector');

    assert.deepEqual(errors, []);
    await page.close();
});

/** Waits for VectorCraft to show one of its own dialogs (ui.inspect's `ui.dialog`). */
async function waitForDialog(frame) {
    const deadline = Date.now() + STEP_TIMEOUT_MS;
    while (Date.now() < deadline) {
        const inspect = await request(frame, 'ui.inspect');
        if (inspect?.ui?.dialog) {
            return;
        }
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw new Error('VectorCraft showed no dialog');
}

test('VectorCraft\'s Open shows the OS dialog, the picked file is bound, and a second open brings its tab to the front', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    await saveNewDrawing(page, id, frame, 'picked.vectorcraft');
    await windowById(page, id).locator('header.window-chrome .chrome-button-close').click();
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    const fresh = await launch(page);
    const pick = async () => {
        await menu(fresh.frame, 'file.open');
        const dialog = page.locator('section.file-dialog[role="dialog"]').last();
        await dialog.getByRole('heading', { name: OPEN_TITLE }).waitFor({ timeout: STEP_TIMEOUT_MS });
        await dialog.getByText('picked.vectorcraft', { exact: true }).dblclick().catch(async error => {
            await shot(page, 'open-dialog');
            throw error;
        });
        await dialog.waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });
    };

    await pick();
    await waitForDocument(fresh.frame, doc => doc.path === 'picked.vectorcraft', 'picked.vectorcraft');
    await expectTitle(page, fresh.id, 'picked.vectorcraft — Kazo Vector');

    // A new drawing in front, then picked.vectorcraft again: its tab comes back, no second copy opens.
    const title = await newModifiedDrawing(fresh.frame);
    await expectTitle(page, fresh.id, `*${title} — Kazo Vector`);
    await pick();
    await expectTitle(page, fresh.id, '*picked.vectorcraft — Kazo Vector');
    const documents = await documentsOf(fresh.frame);
    assert.equal(documents.filter(doc => doc.path === 'picked.vectorcraft').length, 1);
    assert.equal(await vectorWindows(page).count(), 1);

    assert.deepEqual(errors, []);
    await page.close();
});

test('a file named like an open drawing of another folder opens in a new window', { timeout: 500_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    await runInTerminal(page, `mkdir ${HOME}/Work`);
    const { id, frame } = await launch(page);
    await saveNewDrawing(page, id, frame, 'badge.vectorcraft');

    // A second badge.vectorcraft, in Work: written by Save a Copy, so the first window keeps its file.
    await drawRectangle(frame);
    await execute(frame, 'file.saveCopy', { path: 'badge.vectorcraft' });
    const dialog = page.locator('section.file-dialog', { hasText: COPY_TITLE });
    await dialog.waitFor({ timeout: STEP_TIMEOUT_MS });
    await dialog.getByText('Work', { exact: true }).dblclick();
    await dialog.getByText('This folder is empty.').waitFor({ timeout: STEP_TIMEOUT_MS });
    await settleAfterNavigation(page);
    await dialog.getByLabel('File name').fill('badge.vectorcraft');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await dialog.waitFor({ state: 'hidden', timeout: STEP_TIMEOUT_MS });
    await windowById(page, id).locator('.vector-notice', { hasText: 'Copy saved' }).waitFor({ timeout: STEP_TIMEOUT_MS });

    // Decision 9: VectorCraft would take two drawings named badge.vectorcraft for one file.
    await menu(frame, 'file.open');
    const open = page.locator('section.file-dialog[role="dialog"]').last();
    await open.getByRole('heading', { name: OPEN_TITLE }).waitFor({ timeout: STEP_TIMEOUT_MS });
    await open.getByText('Work', { exact: true }).dblclick();
    await open.getByText('badge.vectorcraft', { exact: true }).waitFor({ timeout: STEP_TIMEOUT_MS });
    await settleAfterNavigation(page);
    await open.getByText('badge.vectorcraft', { exact: true }).dblclick();
    await open.waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    const second = await waitForWindow(page, 2, [id]);
    await expectTitle(page, second, 'badge.vectorcraft — Kazo Vector');
    assert.equal((await documentsOf(frame)).length, 1);

    assert.deepEqual(errors, []);
    await page.close();
});

test('an OS chord pressed in the drawing reaches the OS: Alt+Q closes through the close guard', { timeout: 400_000 }, async () => {
    const { page, errors } = await bootPage(browser, host.baseUrl);
    const { id, frame } = await launch(page);
    const title = await newModifiedDrawing(frame);
    await expectTitle(page, id, `*${title} — Kazo Vector`);

    // The keyboard is in VectorCraft's canvas, as when the user draws.
    await frame.locator('#vectorcraft_canvas').click({ position: { x: 600, y: 400 } });
    // Alt+Q is the OS's other Close chord; headless Chromium never delivers the F4 of Alt+F4.
    await page.keyboard.press('Alt+KeyQ');
    const question = page.locator('.message-box-dialog', { hasText: 'Save the changes' });
    await question.waitFor({ timeout: STEP_TIMEOUT_MS });
    await question.getByRole('button', { name: 'No' }).click();
    await windowById(page, id).waitFor({ state: 'detached', timeout: STEP_TIMEOUT_MS });

    assert.deepEqual(errors, []);
    await page.close();
});
