> [Version française](../fr/plan.md)

# Plan: the Kazo Vector app for Kazo OS

Step 0 of the Kazo OS [Craft app port guide](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/craft-app-port-guide.md),
written on 2026-11-14 (`TODO` issue [#1](https://github.com/Kazo-ca/Os-App-Vector/issues/1)).
This document sets the goal, what reading VectorCraft showed, the decisions, the questions asked to
Benoit and the work items. Each work item is written so that it can be handed as is to an
implementation thread. The closest reference is Kazo Photo
([plan](https://github.com/Kazo-ca/Os-App-Photo/blob/photo/base/doc/en/plan.md)): same gap upstream
(no hosted mode), same path (fork, then window). Next come Kazo Fig (several documents in tabs) and
Kazo Pdf (exports received as copies).

## Goal

Kazo Vector is **the vector drawing app of Kazo OS**: paths, shapes, text, gradients, artboards,
`.svg`, `.ai`, `.eps` and `.pdf` files. It wraps the web build of
[VectorCraft](https://github.com/storytold/vectorcraft) (ArtCraft team, `MIT OR Apache-2.0`, Rust
and egui, in alpha) and connects it to the files, dialogs and associations of Kazo OS.

**What sets this port apart**: VectorCraft already has the command API (`window.vectorcraft`,
`craft-web-api`), but **no hosted mode (`craft-web-host`)**. It has to be written, in our fork
[`Kazo-ca/vectorcraft`](https://github.com/Kazo-ca/vectorcraft), before the Kazo window can save
anything to a file of the OS. So Kazo Vector is, from its first version, a **modified** build
compiled from the fork, like Kazo Photo. The fork has a single work item (Kazo Photo had two, the
command API was missing too).

The shared Kazo code (iframe, proxy, file picker, shortcuts, settings, `DocumentSession`, exports)
comes from the `Kazo.Os.AppSdk.Craft` module of Kazo OS
([ADR 0259](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/adr/0259-shared-craft-app-host-module.md),
[ADR 0264](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/adr/0264-craft-app-exports-reach-the-window.md),
guide [`craft-apps.md`](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/framework/craft-apps.md)).
Minimum version: **Kazo OS 3.3.4**, already released.

## Reading VectorCraft (step 0, 2026-11-14)

Read and compiled at the upstream tag `v0.24.1` (commit `f4922aa7`, 2026-11-11). The fork
`Kazo-ca/vectorcraft` exists (`main` = copy of upstream, commit `8998d94c` of 2026-11-13); it does
not have the upstream tags yet.

| Point | Finding |
| --- | --- |
| Web build | `apps/vectorcraft-web` (Trunk, `public_url = "./"`, Cargo profile `release`: "thin" LTO, `wasm-opt -Oz`), eframe on **wgpu**: WebGPU, falls back to WebGL 2 by itself, `?webgl` to force it. Rust 1.95 at least. The 27 languages are in the repository (`crates/ui-egui/src/i18n/*.tsv`): no `craft-i18n` to provide. Rust only, no handwritten JavaScript. |
| Hosted mode | **Absent.** No dependency on `craft-web-host`. `apps/vectorcraft-web/src/web.rs`: Open = the browser's picker (`rfd`), every save = a download (`Services::download`). |
| Command API | **Present**: `window.vectorcraft` (`craft-web-api`, revision `fd5e2fef` of `craft-libs`), **always** installed, without a `?control` parameter. `request`, `execute`, `commands`, `docInspect`, `uiInspect`, `screenshot`, plus five functions of its own: `inspect()`, `openFile(name, bytes)` (opens like a dropped file), `captureDownloads(on)`, `files()`, `readFile(name)` (saves kept in the page instead of being downloaded, 16 at most). A call the page gave up on is not run (`ControlTicket::give_up`). |
| Why `openFile` and `captureDownloads` are not enough | They do not tell the page **when** a save happens, nor whether it is the document or a copy, nor whether the document is modified; and the document calls itself saved as soon as the download starts. A Kazo window built on them would have to poll the page and guess. The `craft-web-host` contract settles these points and the Kazo module already speaks it. |
| Canvas | `vectorcraft_canvas`, hard-coded: one instance per HTML document, so one iframe per window. The page also carries `#vectorcraft_loading` (loading text, then failure messages). |
| Size | `.wasm` of **102.4 MB (97.6 MiB)**, `.js` glue of 188 kB; 37.4 MB gzipped (measured here, and the same size in the official zip `vectorcraft-web-0.24.1.zip`, 38.3 MB). The `README` of `packaging/web` announces 13 MB: it is out of date. Under the Kazo OS limits (128 MiB per file; package of 64 MiB compressed, 128 MiB uncompressed), but **within 2.4% of GitHub's limit** (100 MiB per file in a repository). See "Build size" and question Q1. |
| Open | `io::open_bytes(app, name, bytes, path)` already accepts a path. Files that arrive through the page (`inbox`) are opened with `path = None`. With a path, the engine keeps it only if the format is a save format and reading left nothing aside (`fileio/load.rs`); otherwise the document has "no file" and carries the name as its title. |
| Save | `io::save::save` then `write_plan`. Document formats (`SAVE_FORMATS`): `vectorcraft` (native), `template` (`.vctemplate`), `pdf`, `svg`, `svgz`, `ai`, `eps`. `SavePlan::retargets()` tells a save **of the document** (Save, Save As) from a copy (Save a Copy, Save as Template). One save can write **several files** ("separate artboards" option, linked images). |
| Save on the web | With a path: direct download under the file's name. Without a path: VectorCraft opens its own `saveOptions` dialog (name and format), then the format's options (SVG, PDF), then downloads. Checked in the trial: `file.save` on an SVG opened by `openFile` answers `{ pending: "saveOptions" }`. |
| "Modified" | `SaveJob::complete` marks the document saved **as soon as the download starts**: the browser confirms nothing. This is the guide's pitfall "the app calls itself clean before the write" (craft-libs #611). |
| Exports and other files | All through the same `Services::download`: Export As, Export for Screens (several files in one `.zip`), Save for Web, Package (`.zip`), Save for Office, CSS, reports, and preset files (swatches, styles, libraries, actions, shortcuts, PDF and print presets…). |
| Documents | Several documents in **tabs** in one page. Each document has a stable identifier (`uid`) in the engine, but `ui.inspect` only gives `documents[{ title, dirty }]` and `activeDocument` (the tab's position): neither `uid` nor path. |
| Two tabs, one file | Opening a path that is already open **brings back the existing tab** (`same_open_path`). In the browser, paths are plain names: two files with the same name from two folders would be taken for the same one. |
| Commands | `engine.execute { command, params }`, `ui.menu.invoke`, `engine.commands`, `ui.inspect`, `ui.set`, `ui.dialog.set` / `.confirm` / `.cancel`, `ui.screenshot`. File: `file.new`, `file.open`, `file.save`, `file.saveAs`, `file.saveCopy`, `file.saveAsTemplate`, `file.close`, `file.closeAll`, `file.revert`, `file.exportAs`, `file.print`. Documents: `document.activate { index }`. `file.close` opens a `saveChanges` dialog per modified document. `app.open` and `app.save` ask for file access the web does not have. |
| Theme | `ui.set { brightness }`: `dark`, `mediumDark` (default), `mediumLight`, `light`. Checked in the trial. |
| Language | `prefs.set { key: "interfaceLanguage", value }`, `auto` by default (browser languages, or `?lang=`). 27 catalogs, plus English: `bg bs ca cs da de el es fi fr hi hu id it ja ka ko nl pl pt-br ru sv tr uk vi zh-hans zh-hant`. Checked in the trial (`fr`). |
| Browser picker (`rfd`) | Three uses: Open (one file), Import (several files), Scripts › Other Script. All go through an `input type=file`: the module's picker replaces them (`PickRequested`). **Folder** requests do not exist on the web. |
| Formats opened | Vector documents: `vectorcraft drawcraft vctemplate svg svgz ai ait pdf eps epsf ps dxf emf wmf af afdesign afpub`. Images (opened as a document that contains the image): `png pns jpg jpeg gif webp tif tiff bmp dib rle tga vda icb vst pcx pxr jpf jpx jp2 j2k j2c jpc heic heif psd pdd psb jxl`. Also opened, without creating a document: swatches (`.vcswatches .gpl .ase .acb`), styles (`.vcstyles`), libraries (`.vclibrary`), presets (`.vcflattener .vcpdfpresets .vcprintpresets .vcperspective .vcexportpresets`), shortcut sets (`.kys`), scripts (`.jsx`, checked without being run) and plug-ins (`.wasm`, installed). |
| Settings | **The web build keeps no settings**: in the trial, `localStorage` stays empty after a theme and language change (the `README` of `packaging/web` says the opposite). Every page starts with the default settings. |
| Data recovery | The only use of `localStorage`: backup copies of modified documents (`vectorcraft-recovery/<zone>/<name>`, in base64), offered at the next start after a crash, with Web Locks between tabs. Also written when the graphics rendering is lost. See question Q3. |
| Fonts | In the `.wasm`: Source Sans 3, Source Serif 4, Inter, JetBrains Mono, and BIZ UDPGothic when `CRAFT_FONTS_DIR` is given. Next to the `.wasm` (`fonts/<sha16>/`, checked by SHA-256): the fonts of `crates/text/web-fonts.txt` that the `craft-fonts` clone contains. At the revision of the official build (`b076417b`), these are three fonts (Noto Sans Arabic, Thai and Georgian, 1.3 MB) loaded before start; the other Arabic families of the list are not there yet. No on-demand `fonts/manifest.txt` like PhotoCraft. |
| Rendering lost | If the browser takes the graphics rendering away, the app restarts on a new canvas (three times at most), then gives up with a message in `#vectorcraft_loading`. |
| Printing | File › Print puts the PDF in a hidden iframe and calls the browser's print: the browser's dialog leaves the OS. |
| Clipboard, screen eyedropper, pen | Browser APIs (`EyeDropper`, paste events, Pointer Events): they work in the iframe, which the module opens with `clipboard-read` and `clipboard-write`. |
| Window size | Desktop: 1440 × 900, minimum 800 × 500. |
| Brand | The ArtCraft name and logos are trademarks (`docs/brand/LICENSE-brand.txt`): a modified build must remove them and must not present itself as an ArtCraft product; "based on VectorCraft by the ArtCraft team" in plain text is allowed. Seen in the trial: a "Discord" button in the top bar, on the home screen and in About; links "ArtCraft website", "VectorCraft on getartcraft.com", "Source code on GitHub" on the home screen and in About; "Part of ArtCraft" in About; the dragon icon at the top left and in About (`assets/app-icon`, "the owner's original ArtCraft drawing", licence to read in its `LICENSE.txt`). |
| Upstream repository rules | `AGENTS.md`: Rust only, no `unwrap`/`panic` outside tests, no `unsafe`, everything is a command, never break wasm, no CI change in a feature PR, **no agent mention** in a PR, an issue before a PR. |

### Trial (2026-11-14)

Done at the tag `v0.24.1` (`f4922aa7`), in this session's container (4 cores, Linux), with no change to the code.

| Point | Result |
| --- | --- |
| Tools | Rust stable 1.97.0, target `wasm32-unknown-unknown`, Trunk 0.21.14 (binary), `craft-fonts` at the revision of the official build (`b076417b`, `CRAFT_FONTS_DIR`). |
| Command | `trunk build --release` in `apps/vectorcraft-web`. |
| Duration | 18 min 40 s cold: 14 min of `cargo`, then 4 min of `wasm-opt`. |
| Output | `dist/web`: `index.html`, a `.js` of 188,159 bytes, a `.wasm` of 102,359,248 bytes (129.8 MB before `wasm-opt`; 37.4 MB gzipped), `fonts/` (3 fonts, 1.3 MB), `icons/`, `snippets/`. |
| Start | Chromium without a graphics card (WebGL 2 through SwiftShader, `?webgl`): the app shows its home screen 0.6 s after the page loads from a local server. |
| Page API | `window.vectorcraft` present without a parameter, with the eleven functions. |
| Open | `vectorcraft.openFile("logo.svg", bytes)` opens the SVG in a tab; `ui.inspect` gives `documents: [{ title: "logo.svg", dirty: false }]`. |
| Modify | `shape.rectangle`: `dirty` becomes `true`, the page title becomes `*logo.svg — VectorCraft`. |
| Save | `file.save` answers `{ pending: "saveOptions" }`: without a path, VectorCraft asks for the name and the format. This is what H2 fixes. |
| Theme, language | `ui.set { brightness: "light" }` and `prefs.set { key: "interfaceLanguage", value: "fr" }` change the interface at once. |
| Storage | `localStorage` empty at start and after these changes. |
| Console | No error; one warning "Software rasterizer detected", expected here. |

Screenshots: `vector/trials/` in the project files (outside the repository).

### Build size

The official `.wasm` is too close to GitHub's limit to be stored in a repository: the next
version of VectorCraft can exceed it. Two trials of size settings, made through Cargo environment
variables (`CARGO_PROFILE_RELEASE_*`), so **without changing the code or the fork**:

| Build | `.wasm` | gzip | Cold duration | Result |
| --- | --- | --- | --- | --- |
| Official profile (`opt-level = 3`, "thin" LTO, `wasm-opt -Oz`) | 102,359,248 bytes (97.6 MiB) | 37.4 MB | 18 min 40 s | starts in 0.6 s |
| Size (`opt-level = "s"`, "thin" LTO, `strip`, `wasm-opt -Oz`) | **71,694,182 bytes (68.4 MiB)** | **29.2 MB** | 10 min 30 s (8 min 19 s of `cargo`, 2 min 11 s of `wasm-opt`) | starts in 0.5 s; same trial run passes (open, modify, `file.save`, theme, language) |
| Size with "fat" LTO | — | — | — | linking is killed for lack of memory (more than 14 GiB): not to be kept, GitHub Actions machines have no more |

The "size" build is 30% smaller and leaves a 32% margin under GitHub's limit. Two reservations:

- **Drawing speed was not measured.** `opt-level = "s"` usually makes code a little slower; yet
  PhotoCraft compiles its web build that way (`wasm-release` profile). To measure in work item 2 on
  a large drawing, in both builds.
- With `strip = true`, the section that names the wasm features in use (SIMD, bulk memory)
  disappears and the `wasm-opt` run by Trunk refuses the file. In the trial, `wasm-opt` was run by
  hand with the `--enable-…` options. The script of work item 2 takes `strip = "debuginfo"` or
  passes these options to Trunk, and measures again.

The Store catalog (`Os-AppMarketPlace`) compiles the app at the tag's commit and takes no binary
from the author: so the `.wasm` must be obtainable from that commit, either because it is in it, or
because the .NET build downloads it and checks its SHA-256.

## What must be added to VectorCraft (fork)

The contract is that of `craft-web-host` (`storytold/craft-libs`, version "1"), the same as the
other Craft apps, with the `vectorcraft:` prefix. The code lives in `apps/vectorcraft-web`, modelled
on `apps/photocraft-web/src/{host.rs,hostmode.rs}` of the fork
[`Kazo-ca/photocraft`](https://github.com/Kazo-ca/photocraft/tree/kazo/base) (commit `e136e791`) and
on `apps/gridcraft-web/src/{host.rs,hostmode.rs}`. Three points also need a small opening in
`crates/ui-egui` (marked ★), because the web shell does not see today what it needs.

| # | Behaviour in hosted mode (`data-host="page"` or `?host=parent`) | Why |
| --- | --- | --- |
| H1 | `vectorcraft:ready { version: "1", appVersion, file: false }` when the app listens; `vectorcraft:failed { error }` if it does not start, **and when the rendering is lost for good** (`give_up`). | The Kazo window knows when to send the file and when to show an error. |
| H2 | `vectorcraft:open { name, bytes }` opens the file like File › Open, refused above a limit before being copied. The document opened by the host **receives its name as its path** (`open_bytes(…, Some(name))`); the engine keeps it only for a save format read in full. | With a path, Save rewrites the same file instead of opening the `saveOptions` dialog. |
| H3 | ★ The save **of the document** (`SavePlan::retargets()`) leaves as `vectorcraft:save { name, bytes, saveAs, id }`. `saveAs` is false when the document is written under its own path, true in the other cases (Save As, first save, other format). VectorCraft's `saveOptions` dialog stays: it chooses the format, then the save leaves with `saveAs` true. | The host knows whether it rewrites the bound file or must ask where to save. |
| H4 | ★ The document is marked saved (`SaveJob::complete`) only on receiving `vectorcraft:saved { id }`; without an answer, it stays modified. The `SaveJob` keeps the state of the document at the time of the save: a late confirmation does not mark clean a document modified since. | Fixes for VectorCraft the pitfall "the app calls itself clean before the write" (craft-libs #611). |
| H5 | **Copies, templates, exports, packages and preset files** keep the download (`<a download>`). A save of the document that writes **several files** (separate artboards, linked images) sends the document's file to the host and leaves the others as downloads. | The Kazo module intercepts downloads and hands them to the window as copies (ADR 0264); nothing to invent. |
| H6 | `vectorcraft:dirty { dirty }` when one of the open documents has changes, or has none any more. | Window title. |
| H7 | ★ `ui.inspect` gives for each document `id` (its `uid`), `path` and `active`, on top of `title` and `dirty`: the shape of WordCraft, which the module's `OpenDocument.ListFrom` already reads. | Bind each tab to its file without relying on the tab's position or on the title. |
| H8 | Outside hosted mode, nothing changes (downloads, browser picker, `openFile`, `captureDownloads`). | The patch stays proposable upstream. |
| H9 | In hosted mode: no "Discord" button (top bar, home, About), no links to ArtCraft on the home screen; About names the project in plain text, without "Part of ArtCraft"; the dragon icon is checked against its licence (`assets/app-icon/LICENSE.txt`, `docs/brand/`) and removed from the interface if it is a trademark. | Modified build. |
| H10 | In hosted mode (Q3 = A), data recovery does not write to `localStorage`. | The browser's storage is shared between Kazo users and with the OS. |

Tests asked for in the fork: unit tests of the pure rules (`saveAs`, confirmation, size limit, shape
of `ui.inspect`), `cargo fmt --check`, `cargo clippy -D warnings`, `cargo xtask layers`,
`cargo xtask wasm`, a Trunk build, and a Playwright run on a test page that plays the host (`open`,
`save`, `saved`, `dirty`, `failed`, downloaded copy).

## Decisions

| # | Subject | Decision |
| --- | --- | --- |
| 1 | Name and identity | "**Kazo Vector**". Id `ca.kazo.vector`, publisher `pub.kazo`, assembly `Kazo.Vector`, category `graphics` (that of Kazo Fig and Kazo Photo), several windows, window of 1280 × 820, minimum content 800 × 500. |
| 2 | Shared code | Module `Kazo.Os.AppSdk.Craft`, **Kazo OS 3.3.4** at least (exports of ADR 0264 included). Submodule `external/KazoOs` at the tag `v3.3.4`, packages from nuget.org: no OS branch to follow. |
| 3 | Screen change | `canChangeScreen: false` in the manifest from work item 2 (issue [#2](https://github.com/Kazo-ca/Os-App-Vector/issues/2), ADR 0263). |
| 4 | Hosted mode | In our fork now, and proposed upstream in parallel: Benoit's answer to the same question for Kazo Photo (Q1 = A, 2026-11-13), reused here. |
| 5 | Origin of the build | Always compiled from the fork: branch `kazo/v0.24.1` (= upstream tag `v0.24.1`, commit `f4922aa7`), integration branch `kazo/base` created from it. Submodule `external/vectorcraft` on `kazo/base`. Trunk output (`dist/web`, not the zip of `packaging/web/package.sh`) with `vectorcraft.json` (version, commit, names and SHA-256). Where the `.wasm` lives: question Q1. |
| 6 | Iframe page | `wwwroot/host/index.html`: canvas `vectorcraft_canvas` with `data-host="page"`, `data-craft-app="vectorcraft"`, `data-craft-build="../vectorcraft/vectorcraft.json"`, plus the `#vectorcraft_loading` element of the upstream page. No `control` parameter: `window.vectorcraft` is always there. |
| 7 | Tabs | We keep VectorCraft's tabs: one window holds several documents, each tab bound to its file; a double-click in the explorer opens a new window. Benoit's answer for Kazo Photo (Q2 = A), reused here. |
| 8 | Saving | `Saving`: the module's rule (`DocumentSession.Decide(name, saveAs)`), the window returns `true` only when the file is written, which triggers `saved` (H4). `Exporting`: always a copy (`DecideCopy`). |
| 9 | Two files with the same name | A name is bound only once per window. Opening a file whose name is already bound in this window opens it in a **new window**: otherwise VectorCraft would bring back the other file's tab. |
| 10 | Close guard | Read from `ui.inspect` (H7); the window activates each modified document (`document.activate`), runs `file.save` and waits for the end of **its** writes. |
| 11 | Theme and language | `ui.set { brightness }`: `mediumDark` (VectorCraft's default) for the dark theme of the OS, `light` for the light one, at start and at every change. Language: `prefs.set` of `interfaceLanguage` at start (`CraftLanguage.Pick` over the 27 codes). |
| 12 | Settings | The web build keeps none (see the reading): nothing to store per user in v1, and every window starts with the default settings, the theme and the language of the OS. Limit written in the listing. Having VectorCraft keep them is a request to upstream. |
| 13 | Shortcuts | `ReservedKeys.Os` **without** `Ctrl+N`: in a tabbed editor, `Ctrl+N` is VectorCraft's "New document". Conflicts between the chords of the OS and those of VectorCraft are listed in work item 3 and noted in the listing. |
| 14 | Fonts | The build follows the official build: `CRAFT_FONTS_DIR` on `craft-fonts` at revision `b076417b`, that is the built-in fonts and three served fonts (1.3 MB, OFL licences). No other fonts in v1. |
| 15 | Printing | File › Print keeps the browser's print dialog in v1. Limit written in the listing. |
| 16 | Large files | The Kazo OS limit (128 MiB per file, ADR 0245) applies; a larger file is refused with a message. |
| 17 | Upstream track | The issue texts (in English, with no agent mention) are prepared in `/mnt/project-files/vector/upstream/` by the fork thread; Benoit publishes them. The fork's branches are written so that they can become upstream PRs. |
| 18 | Kazo OS ADR | None planned: the module already has what is needed. If a work item needs one, next free number **0271** on 2026-11-14 (to check again on `main`, the open PRs and every `*/base` branch when writing it; Kazo Deck and Kazo Design move in parallel). |

## Questions asked to Benoit

Asked in the thread on 2026-11-14. **Benoit answered A to all three the same day**:

- **Q1 = A**: build compiled with the size settings and kept in the repository; the script fails
  above 90 MiB.
- **Q2 = A**: default app for the vector formats nothing else opens, "Open with" for `.pdf` and
  `.afdesign`, nothing for images.
- **Q3 = A**: data recovery turned off in v1 (H10), with a `TODO` issue to redo it later in the
  user's private folder: [#4](https://github.com/Kazo-ca/Os-App-Vector/issues/4). Benoit asked for
  the same issue for Kazo Photo
  ([Os-App-Photo#11](https://github.com/Kazo-ca/Os-App-Photo/issues/11)) and Kazo Fig
  ([Os-App-Fig#9](https://github.com/Kazo-ca/Os-App-Fig/issues/9)).

The questions as they were asked:

**Q1. Where does the VectorCraft build live?** Kazo Photo keeps its build in the repository (39 MB)
because the Store does not compile Rust. Here the official `.wasm` weighs 102 MB, within 2.4% of
GitHub's limit (100 MiB per file).

- **A (recommended)**: compile with the size settings and **keep the build in the repository**,
  like Kazo Photo. `.wasm` of 72 MB, 32% under the limit; nothing to change in the catalog or in
  the fork. The build script fails if the `.wasm` exceeds 90 MiB, and we then move to B. The cost:
  every VectorCraft update adds about 70 MB to the repository's history, and drawing can be a
  little slower (measured in work item 2; if the gap is a nuisance, we move to B with the official
  profile).
- B: keep the official profile (102 MB) and **do not put the `.wasm` in the repository**. A
  workflow compiles it and publishes it as a file of a GitHub release of this repository; the
  repository only keeps `vectorcraft.json` (name, size, SHA-256), and the .NET build downloads the
  file and checks it. The repository stays light and the speed is upstream's, but it is one more
  mechanism to write and test (longer work item 2), the catalog then compiles with a downloaded
  file (pinned by its SHA-256) and a build without network no longer works.
- C: Git LFS. The repository stays light without a home-made mechanism, but LFS must be turned on
  in the catalog's build, and LFS has a storage and transfer quota that every CI build
  draws 100 MB from.

**Q2. Formats associated with Kazo Vector.** Today no app opens `.svg` by default (Kazo Photo and
Kazo Fig offer it in "Open with"). Kazo Pdf opens `.pdf`. Kazo Photo is the default app for Affinity
files (`.af .afdesign .afpub`), `.psd` and images.

- **A (recommended)**: default app for the vector formats nothing else opens:
  `.vectorcraft .vctemplate .svg .svgz .ai .ait .eps .epsf .ps .dxf .emf .wmf`. "Open with" only
  for `.pdf` (Kazo Pdf stays the default app) and `.afdesign` (Kazo Photo already has it). Nothing
  for images (`.png`, `.jpg`, `.psd`…): Kazo Vector places them in a document through
  File › Import, it does not have to open them on a double-click.
- B: like A, and "Open with" for images too. The "Open with" menu of every photo grows by one app
  that is not made for that.
- C: default app only for `.vectorcraft` and `.vctemplate`, everything else in "Open with". A
  double-click on an `.svg` or an `.ai` then opens nothing until the user has chosen.

**Q3. Data recovery after a crash.** VectorCraft keeps backup copies of modified documents in the
browser's storage (`localStorage`). In Kazo OS this storage is that of the OS site: shared by all
Kazo users of the same browser, limited to about 5 MB, and used by the OS itself.

- **A (recommended)**: turned off in v1 (H10). After a browser crash, unsaved work is lost, as in
  Kazo Photo and Kazo Fig. No user's document is offered to another, and the OS keeps its storage.
- B: left as is. Recovery works, but a Kazo user can be offered another user's document, and large
  copies can fill the storage the OS needs.
- C: backup copies stored in the Kazo user's private folder. This is the right solution in the
  long run, but it needs one more exchange between VectorCraft and the window (outside the
  `craft-web-host` contract): one more work item in the fork and in the window, to do after v1.

## Work items

Two repositories: the fork `Kazo-ca/vectorcraft` (Rust) and `Kazo-ca/Os-App-Vector` (C#). In each,
one integration branch, one branch and one PR per work item; one implementation thread per work
item. Benoit merges `vector/base` into `main`; the fork's `main` stays the copy of upstream.

| No. | Repository, branch (to) | Content | Depends on |
| --- | --- | --- | --- |
| 1 | Os-App-Vector, `vector/1-plan` (`vector/base`) | this plan (fr and en) | — |
| F1 | vectorcraft, `kazo/1-web-host` (`kazo/base`) | H1 to H10: hosted mode | plan merged |
| 2 | Os-App-Vector, `vector/2-solution` (`vector/base`) | solution, manifest, compilation from the fork, iframe page, window that starts | plan merged; "done" waits for F1 |
| 3 | Os-App-Vector, `vector/3-window` (`vector/base`) | open, save, tabs, exports, close guard, picker, shortcuts, theme, language | 2 and F1 |
| 4 | Os-App-Vector, `vector/4-store` (`vector/base`) | listing, README, licences, measurements, tag, catalog, `features.md` | 3 |

**Order**: F1 in one thread, work item 2 in a second thread, at the same time; then 3; then 4.
Kazo OS 3.3.4 is released: work item 4 waits for no OS version.

### F1. Hosted mode (fork)

- Create `kazo/v0.24.1` on the upstream commit `f4922aa7` (`git fetch https://github.com/storytold/vectorcraft refs/tags/v0.24.1`), then `kazo/base` from that branch.
- `apps/vectorcraft-web/src/host.rs` and `hostmode.rs` modelled on the PhotoCraft fork: `Host::connect("vectorcraft", canvas)`, listening to `open` and `saved`, `post_ready`, `post_save`, `post_dirty`, `post_failed`. `craft-web-host` pinned to a revision of `storytold/craft-libs`, like `craft-web-api`.
- The ★ openings in `crates/ui-egui`, as small as possible and useful outside Kazo: a "document write" service next to `download` (H3), the confirmation that calls `SaveJob::complete` (H4), `id`, `path` and `active` in `ui.inspect` (H7), a "hosted" flag for H9. Read `io/save.rs` (`write_plan`, `save`), `io/open.rs` (`open_bytes`), `background.rs` and `control.rs` before choosing the shape; the PhotoCraft fork shows a shape that works (`Services::write_document`, `DocumentWrite`, `confirm_document_write`, `Services::hosted`).
- H9, H10, and a line in `docs/development.md` ("Web build") that describes hosted mode.
- Drafts of the upstream issues in `/mnt/project-files/vector/upstream/` (hosted mode; `id` and `path` in `ui.inspect`; settings not kept on the web; `README` of `packaging/web` that announces 13 MB), in English, with no agent mention.
- **Done when**: the test page that plays the host opens an SVG and a `.vectorcraft`, receives `save` with the right `saveAs` in the four cases (rewriting an SVG, first save of a new document, Save As, format change), the document becomes clean again only after `saved`, Save a Copy and Export As still download, and the same page without `data-host` behaves as before. The thread gives the commit of `kazo/base` to pin.

### 2. Solution and compilation (`vector/2-solution`)

- Solution renamed (`Kazo.Vector`, `Kazo.Vector.Os`, `Kazo.Vector.Tests`), `[AppManifest]` of decision 1, `canChangeScreen: false`, capabilities of Kazo Photo, associations of Q2 = A.
- Submodule `external/KazoOs` at `v3.3.4`, submodule `external/vectorcraft` on `kazo/base`.
- `tools/build-vectorcraft.sh` (current stable Rust, target `wasm32-unknown-unknown`, Trunk 0.21.14, `CRAFT_FONTS_DIR` on `craft-fonts` at `b076417b`, `trunk build --release` in `apps/vectorcraft-web`, size settings of Q1 = A: `opt-level = "s"`, "thin" LTO, failure above 90 MiB; the build is kept in `wwwroot/vectorcraft`), workflow `build-vectorcraft.yml`, `vectorcraft.json`, xUnit test "the commit of `vectorcraft.json` is that of the submodule". Models: `Os-App-Photo` (`photo/base`) and `Os-App-PDF`.
- Iframe page (decision 6) and window on `CraftFrame` and `FrameStartup`: loading, app, error, "Retry"; theme and language (decision 11).
- Until F1 is merged, compile `kazo/v0.24.1` to break in the tooling; the run that waits for `ready` comes with F1 (then move the pin to `kazo/base`).
- **Done when**: the app starts in WebGPU and in WebGL 2, two windows run together, the `.kazoapp` package installs in Kazo OS 3.3.4; package size, start time and memory per window noted here.

### 3. Window (`vector/3-window`)

- Opening: file asked for at launch, `OpenAsync`, decision 9. Files that create no document (swatches, libraries, presets…): sent to VectorCraft without a binding. VectorCraft's File › Open shows the OS dialog (`PickRequested`).
- Saving: decision 8; format list (`CraftDocumentFormats`: `vectorcraft vctemplate pdf svg svgz ai eps`); the four cases of F1 end to end in Kazo OS; a cancelled dialog or a refused write leaves the document modified.
- Tabs (decision 7): binding of each document (`id` of H7) to its path, window title on the active tab, close guard (decision 10).
- Exports as copies (`Exporting`, `.zip` included), file picker (one or several files), shortcuts (decision 13), live theme.
- Measurement of a large file (an SVG or an `.ai` of several tens of MB): open and save time, memory.
- **Done when**: no case loses changes without the user having chosen it, no file dialog or download of the browser leaves the app (printing excepted, decision 15), and the OS shortcuts work with the cursor in the drawing.

### 4. Store (`vector/4-store`)

- Icon of this repository's own, with no ArtCraft mark; listing fr and en ("based on VectorCraft by the ArtCraft team", known limits: alpha, 128 MiB per file, settings not kept, no recovery after a crash (issue #4), printing through the browser, window that stays on its screen, download size); screenshots; README redone from the listing (`store:readme`, "Try" with `?autolaunch=ca.kazo.vector`); `THIRD-PARTY-NOTICES` (VectorCraft, its notices, OFL fonts).
- Run in a released Kazo OS 3.3.4 (`tests/installed`), "Tag a release" workflow, PR to the `Os-AppMarketPlace` catalog (signing happens there, no key here), `features.md` row in `Kazo-ca/Os`.
- **Done when**: Kazo Vector installs from the Store, opens an `.svg` on a double-click and saves it.

## Upstream track

Drafts in English, published by Benoit, in `/mnt/project-files/vector/upstream/` (written by the
F1 thread):

- VectorCraft: hosted mode of the web build (`craft-web-host`), with H1 to H6;
- VectorCraft: `id`, `path` and `active` in the documents of `ui.inspect`;
- VectorCraft: the web build does not keep settings; the `README` of `packaging/web` announces
  13 MB for a `.wasm` of 102 MB;
- `craft-libs`: join #611 (save confirmation) and #610 (`open-request`).

Not checked: the issues already open upstream on these subjects (`storytold` is out of the
session's reach for the GitHub API); Benoit looks before publishing.

## Progress

- [x] Step 0: reading VectorCraft 0.24.1, compiling and trying the web build (2026-11-14).
- [x] Plan fr and en.
- [x] Benoit's answers to Q1 to Q3: A, A, A (2026-11-14); issue #4 for data recovery.
- [ ] F1: hosted mode in the fork.
- [ ] Work item 2: solution, manifest, compilation from the fork, iframe page, window.
- [ ] Work item 3: window, bound open and save, tabs, exports, close guard.
- [ ] Work item 4: Store listing, README, third-party notices, run in the released OS.
- [ ] Release: `vector/base` into `main` (Benoit), tag `v0.1.0`, catalog, `features.md`.
