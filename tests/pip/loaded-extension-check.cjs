/* Loads the real MV3 extension into an isolated Chrome for Testing profile. Native
 * transport is stubbed in the test copy, without touching owner registry/profile. */
"use strict";
const fs = require("node:fs"), path = require("node:path"), assert = require("node:assert/strict");
const execFileAsync = require("node:util").promisify(require("node:child_process").execFile);
const root = path.resolve(__dirname, "../..");
const { chromium } = require(process.env.MORF_PLAYWRIGHT_MODULE);
const destination = path.join(root, "artifacts/pip-loaded-extension/extension");
fs.mkdirSync(destination, { recursive: true });
for (const name of ["manifest.json", "worker.js", "policy.js", "content.js", "inline.js", "youtube-media.js", "icon128.png"]) fs.copyFileSync(path.join(root, "browser-extension", name), path.join(destination, name));
const bootstrap = `
globalThis.__nativeMessages = [];
chrome.runtime.connectNative = () => {
  const messages = new Set(), disconnected = new Set();
  globalThis.__deliverNative = value => { for (const fn of messages) fn(value); };
  return {
    onMessage: { addListener: fn => messages.add(fn) },
    onDisconnect: { addListener: fn => disconnected.add(fn) },
    postMessage: value => {
      globalThis.__nativeMessages.push(value);
      if (value.type === "hello") setTimeout(() => { for (const fn of messages) fn({ version:1, type:"config", enabled:true, seekSeconds:10, advanced:true }); }, 0);
    },
    disconnect: () => { for (const fn of disconnected) fn(); }
  };
};
`;
fs.writeFileSync(path.join(destination, "worker.js"), bootstrap + fs.readFileSync(path.join(destination, "worker.js"), "utf8"));
(async () => {
  const profile = fs.mkdtempSync(path.join(root, "artifacts/pip-loaded-extension/profile-"));
  const context = await chromium.launchPersistentContext(profile, {
    executablePath: process.env.MORF_CHROMIUM_EXECUTABLE, headless: process.env.MORF_HEADED !== "1",
    args: ["--enable-unsafe-extension-debugging", "--disable-extensions-except=" + destination, "--load-extension=" + destination, "--autoplay-policy=no-user-gesture-required"]
  });
  try {
    context.setDefaultTimeout(8000);
    const worker = context.serviceWorkers()[0] || await context.waitForEvent("serviceworker");
    assert.ok(worker.url().includes("kobpckacdnbgcaedocbpbplmkfdmfigd"));
    console.log("PASS Original MV3 manifest loads with its registered native-host extension identity");
    assert.equal((await worker.evaluate(() => globalThis.__nativeMessages)).length, 0);
    console.log("PASS Installed but unused extension opens no native connection");
    const page = await context.newPage();
    const liveUrl = process.env.MORF_PIP_LIVE_URL;
    const inline = !!liveUrl || process.env.MORF_PIP_INLINE === "1";
    if (!liveUrl) {
    await page.route("**/*", route => route.fulfill({ contentType: "text/html; charset=utf-8", body: '<!doctype html><meta charset="utf-8"><title>Loaded extension video</title><div id="movie_player"><div class="html5-video-container"><video muted style="width:640px;height:360px"></video></div><div class="ytp-right-controls"></div></div>' }));
    await page.goto(inline ? "https://www.youtube.com/watch?v=morf-inline-fixture" : "https://video.morf.test/player");
    await page.evaluate(async () => {
      const canvas = document.createElement("canvas"); canvas.width = 640; canvas.height = 360;
      const drawing = canvas.getContext("2d"), stream = canvas.captureStream(12);
      const recorder = new MediaRecorder(stream, { mimeType: "video/webm;codecs=vp8" }), chunks = [];
      recorder.ondataavailable = event => chunks.push(event.data);
      const done = new Promise(resolve => { recorder.onstop = resolve; });
      recorder.start();
      for (let frame = 0; frame < 24; frame++) {
        drawing.fillStyle = frame % 2 ? "#283854" : "#21314d";
        drawing.fillRect(0, 0, 640, 360);
        await new Promise(resolve => setTimeout(resolve, 83));
      }
      recorder.stop(); await done; stream.getTracks().forEach(track => track.stop());
      const video = document.querySelector("video");
      video.loop = true;
      video.src = URL.createObjectURL(new Blob(chunks, { type: "video/webm" }));
      navigator.mediaSession.metadata = new MediaMetadata({ title: "Loaded extension video", artist: "Morf checks" });
      await video.play();
    });
    } else {
      await page.goto(liveUrl, { waitUntil: "domcontentloaded", timeout: 30000 });
      await page.waitForSelector("video", { timeout: 20000 });
      // This is a disposable test profile. Decline optional cookies if YouTube's
      // regional consent dialog would otherwise pause or cover the fixture.
      const reject = page.getByText(/^(Tümünü reddet|Reject all)$/i);
      try { await reject.first().click({ timeout: 6000 }); await reject.first().waitFor({ state: "hidden" }); }
      catch { /* No consent dialog in this region/profile. */ }
      await page.bringToFront();
      await page.locator("video").first().evaluate(video => { video.muted = true; return video.play(); });
      try {
        await page.waitForFunction(() => {
          const video = document.querySelector("#movie_player video") || document.querySelector("video");
          return video && !video.paused && video.currentTime > 1;
        }, null, { timeout: 15000 });
      } catch (error) {
        console.log("LIVE PLAYBACK UNAVAILABLE", await page.evaluate(() => {
          const video = document.querySelector("#movie_player video") || document.querySelector("video");
          return { title: document.title, paused: video?.paused, time: video?.currentTime, ready: video?.readyState,
            mediaError: video?.error?.code, playerMessage: document.querySelector(".ytp-error")?.textContent?.trim().slice(0, 400),
            buttons: [...document.querySelectorAll("tp-yt-paper-dialog button, ytd-consent-bump-v2-lightbox button")].map(button => ({ text: button.textContent.trim(), label: button.getAttribute("aria-label") })) };
        }));
        await page.screenshot({ path: path.join(root, "artifacts/pip-loaded-extension/live-playback-unavailable.png") });
        throw error;
      }
    }
    await page.waitForFunction(() => document.querySelector("video").readyState > 0);
    if (liveUrl) console.log("SOURCE BEFORE", await page.evaluate(() => {
      const video = document.querySelector("video"); globalThis.__mediaEvents = [];
      for (const type of ["pause", "play", "emptied", "loadedmetadata"]) video.addEventListener(type, () => globalThis.__mediaEvents.push({ type, paused:video.paused, time:video.currentTime }));
      return { paused:video.paused, time:video.currentTime, ready:video.readyState };
    }));
    if (liveUrl) {
      assert.equal(await page.evaluate(() => globalThis.__morfYoutubeMedia), true);
      const recommendation = page.locator("#related a[href*='/watch?']").first();
      if (await recommendation.count()) {
        const before = await page.evaluate(() => ({ title: navigator.mediaSession.metadata?.title,
          artwork: JSON.stringify(navigator.mediaSession.metadata?.artwork) }));
        await recommendation.hover();
        let previewStarted = false;
        try {
          await page.waitForFunction(() => [...document.querySelectorAll("video")].some(video => !video.closest("#movie_player") && !video.paused && video.readyState > 1), null, { timeout: 4000 });
          previewStarted = true;
        } catch { /* The isolated profile/site may not autoplay recommendations. */ }
        const after = await page.evaluate(() => ({ title: navigator.mediaSession.metadata?.title,
          artwork: JSON.stringify(navigator.mediaSession.metadata?.artwork) }));
        assert.deepEqual(after, before);
        console.log((previewStarted ? "PASS" : "UNVERIFIED autoplay preview; stable metadata on hover:") + " Live YouTube recommendation hover retains the main video's title and artwork");
        await page.locator("#movie_player").hover();
      } else console.log("UNVERIFIED Live recommendation hover: this page supplied no related watch link");
    }
    await page.bringToFront();
    const extensionCdp = await context.browser().newBrowserCDPSession();
    const { targetInfos } = await extensionCdp.send("Target.getTargets", { filter: [{ type: "tab" }] });
    const tabTarget = targetInfos.find(target => target.url === page.url());
    assert.ok(tabTarget, JSON.stringify(targetInfos));
    const pipEvent = context.waitForEvent("page");
    pipEvent.catch(() => {});
    if (inline) {
      const control = page.locator('button[id^="morf-pip-"][id$="-inline"]');
      try { await control.waitFor({ state: "attached" }); }
      catch (error) {
        console.log("INLINE", await page.evaluate(() => ({ button: document.querySelector(".ytp-right-controls")?.outerHTML,
          width: document.querySelector("video")?.videoWidth, visibility: document.visibilityState })),
          await worker.evaluate(() => ({ messages: globalThis.__nativeMessages, selected: selected?.ready })));
        throw error;
      }
      if (process.env.MORF_PIP_UIA_EXE) {
        const { processInfo } = await extensionCdp.send("SystemInfo.getProcessInfo");
        const browserProcess = processInfo.find(process => process.type === "browser").id;
        const session = await worker.evaluate(() => selected.session);
        const command = `
          $ErrorActionPreference = 'Stop'
          $assembly = [Reflection.Assembly]::LoadFrom($env:MORF_PIP_UIA_EXE)
          $messageType = $assembly.GetType('Widgates.PipMessage', $true)
          $message = [Activator]::CreateInstance($messageType, $true)
          $message.Session = $env:MORF_TEST_PIP_TOKEN
          $message.BrowserProcess = [uint32]$env:MORF_TEST_PIP_BROWSER
          $method = $assembly.GetType('Widgates.PictureInPicturePlacement', $true).GetMethod('InvokeInlineAsync', [Reflection.BindingFlags]'Static,NonPublic')
          $signal = [Threading.ManualResetEvent]::new($true)
          $predicate = [Delegate]::CreateDelegate([Func[bool]], $signal, $signal.GetType().GetMethod('WaitOne', [type[]]@()))
          $task = $method.Invoke($null, @($message, $predicate))
          if (!$task.GetAwaiter().GetResult()) { exit 2 }
        `;
        const invoke = () => execFileAsync("powershell.exe", ["-NoProfile", "-Command", command], {
          env: { ...process.env, MORF_TEST_PIP_TOKEN: session, MORF_TEST_PIP_BROWSER: String(browserProcess) }, timeout: 15000
        });
        try { await invoke(); }
        catch (error) {
          const pageCdp = await context.newCDPSession(page);
          const ax = await pageCdp.send("Accessibility.getFullAXTree");
          console.log("INLINE ACCESSIBILITY", ax.nodes.filter(node => node.name?.value === "Morf · Pencere içinde video").map(node => node.role?.value));
          throw error;
        }
      } else {
        await page.locator("#movie_player video").hover();
        await control.click();
      }
    } else {
      await extensionCdp.send("Extensions.triggerAction", { id: "kobpckacdnbgcaedocbpbplmkfdmfigd", targetId: tabTarget.targetId });
    }
    try { await page.waitForFunction(() => document.querySelector("video") === null || [...document.documentElement.children].some(element => element.shadowRoot)); }
    catch (error) { console.log("DIAGNOSTICS", await worker.evaluate(() => ({ messages: globalThis.__nativeMessages, state: typeof selected === "undefined" ? null : selected && { ready: selected.ready, starting: selected.starting, closed: selected.closed } }))); throw error; }
    const messages = await worker.evaluate(() => globalThis.__nativeMessages);
    assert.equal(messages[0]?.type, "hello"); assert.ok(messages.some(value => value.type === "status"));
    if (inline) assert.ok(messages.some(value => value.type === "status" && value.inline && !value.open));
    console.log(inline ? "PASS Inline player attaches and publishes status without a toolbar click" : "PASS Real extension action grants activeTab and publishes only the selected video's status");
    const pip = await pipEvent;
    assert.ok(pip); await pip.waitForSelector("video");
    const before = await pip.locator("video").evaluate(video => video.currentTime);
    try { await pip.waitForFunction(before => { const video = document.querySelector("video"); return !video.paused && video.currentTime > before + 0.5; }, before); }
    catch (error) {
      console.log("VIDEO DIAGNOSTICS", await pip.evaluate(() => { const v = document.querySelector("video"); return v && { paused:v.paused, time:v.currentTime, ready:v.readyState, network:v.networkState, duration:v.duration, error:v.error?.code, source:!!v.currentSrc, width:v.videoWidth }; }));
      if (liveUrl) console.log("MEDIA EVENTS", await page.evaluate(() => globalThis.__mediaEvents));
      await page.screenshot({ path: path.join(root, "artifacts/pip-browser-checks/live-source.png") });
      await pip.screenshot({ path: path.join(root, "artifacts/pip-browser-checks/live-pip.png") });
      throw error;
    }
    console.log("PASS Video playback advances inside the real isolated-world PiP window");
    await page.bringToFront();
    await worker.evaluate(() => globalThis.__deliverNative({ version: 1, type: "command", session: globalThis.__nativeMessages[0].session, command: "open" }));
    await pip.waitForFunction(() => document.hasFocus());
    console.log("PASS A linked Morf artwork command focuses the existing PiP without recreating playback");
    if (liveUrl) {
      await pip.locator("main").hover();
      const seekBefore = await pip.locator("video").evaluate(video => video.currentTime);
      await pip.getByRole("button", { name: "10 saniye ileri", exact: true }).click();
      const seekAfter = await pip.locator("video").evaluate(video => video.currentTime);
      assert.ok(seekAfter > seekBefore + 8);
      console.log("PASS Live site PiP seeks forward by the configured ten seconds");
      if (process.env.MORF_PIP_SCREENSHOT) {
        await pip.setViewportSize({ width: 560, height: 315 });
        await pip.waitForFunction(() => !document.querySelector("video").seeking);
        await pip.locator("main").hover();
        await pip.waitForFunction(() => getComputedStyle(document.querySelector(".overlay")).opacity === "1");
        await pip.screenshot({ path: process.env.MORF_PIP_SCREENSHOT });
      }
      if (process.env.MORF_PIP_TEST_NEXT === "1") {
        const next = pip.getByRole("button", { name: "Sonraki video", exact: true });
        assert.ok(await next.isVisible(), "Live YouTube fixture must expose its next-video action");
        const sourceUrl = page.url();
        await next.click();
        await page.waitForURL(url => url.href !== sourceUrl, { timeout: 15000 });
        assert.ok(!pip.isClosed(), "A same-tab next-video action must keep the PiP window");
        await pip.waitForFunction(() => { const video = document.querySelector("video"); return video && !video.paused && video.currentTime > 0.2; }, null, { timeout: 15000 });
        console.log("PASS Live YouTube next-video navigation retains PiP and resumes the next source");
      }
    }
    await pip.locator("main").hover();
    await pip.getByRole("button", { name: "Duraklat ve PiP'yi kapat", exact: true }).click();
    await page.waitForFunction(() => document.querySelector("video")?.paused === true);
    console.log("PASS Real isolated-world extension can close PiP and restore/pause its selected source");
    console.log(JSON.stringify({ browser: await worker.evaluate(() => navigator.userAgent), transport: "simulated Native Messaging",
      entry: process.env.MORF_PIP_UIA_EXE ? "Windows UI Automation" : inline ? "inline button" : "toolbar action" }));
  } finally { await context.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
