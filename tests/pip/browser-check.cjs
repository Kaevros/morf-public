/* Isolated Chromium integration checks. Native Messaging is simulated here; C# checks
 * exercise the real current-user pipe independently. No owner profile/settings change. */
"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../..");
const modulePath = process.env.MORF_PLAYWRIGHT_MODULE;
if (!modulePath) throw new Error("Set MORF_PLAYWRIGHT_MODULE to an installed Playwright module.");
const { chromium } = require(modulePath);
const output = path.resolve(root, "artifacts/pip-browser-checks");
fs.mkdirSync(output, { recursive: true });
const token = "0123456789abcdef0123456789abcdef";
let passed = 0;
const check = (value, label) => { assert.ok(value, label); console.log("PASS " + label); passed++; };

(async () => {
  const browser = await chromium.launch({ executablePath: process.env.MORF_CHROMIUM_EXECUTABLE, headless: process.env.MORF_HEADED !== "1",
    args: ["--autoplay-policy=no-user-gesture-required"] });
  try {
    const context = await browser.newContext({ viewport: { width: 1000, height: 700 }, reducedMotion: "reduce" });
    context.setDefaultTimeout(7000);
    const page = await context.newPage();
    const errors = []; page.on("pageerror", error => errors.push(error.message));
    await page.route("**/*", route => route.fulfill({ contentType: "text/html; charset=utf-8", body: `<!doctype html><html lang="tr"><head><meta charset="utf-8"><title>Morf test video</title></head><body style="background:#16181d;color:white;font:16px Segoe UI;padding:20px"><h1>Video test fixture</h1><div id="source"><video style="width:640px;height:360px" muted loop></video></div><button id="start">PiP aç</button><button id="native">Morf görsel tıklaması</button></body></html>` }));
    await page.goto("https://video.morf.test/player");
    await page.evaluate(async () => {
      const handlers = new Set(); window.__messages = [];
      window.chrome.runtime = {
        sendMessage: async message => { window.__messages.push(message); },
        onMessage: { addListener: fn => handlers.add(fn), removeListener: fn => handlers.delete(fn) }
      };
      window.__deliver = message => { for (const fn of [...handlers]) fn(message); };
      window.__listenerCount = () => handlers.size;
      const canvas = document.createElement("canvas"); canvas.width = 640; canvas.height = 360;
      const drawing = canvas.getContext("2d"), stream = canvas.captureStream(10);
      const recorder = new MediaRecorder(stream, { mimeType: "video/webm;codecs=vp8" }), chunks = [];
      recorder.ondataavailable = event => chunks.push(event.data);
      const done = new Promise(resolve => { recorder.onstop = resolve; }); recorder.start();
      for (let i = 0; i < 12; i++) {
        drawing.fillStyle = i % 2 ? "#234775" : "#244d65"; drawing.fillRect(0, 0, 640, 360);
        drawing.fillStyle = "white"; drawing.font = "32px sans-serif"; drawing.fillText("Morf · PiP test video", 130, 175);
        await new Promise(resolve => setTimeout(resolve, 100));
      }
      recorder.stop(); await done; stream.getTracks().forEach(track => track.stop());
      const video = document.querySelector("video");
      video.src = URL.createObjectURL(new Blob(chunks, { type: "video/webm" }));
      await new Promise(resolve => { video.onloadeddata = resolve; }); await video.play();
      navigator.mediaSession.metadata = new MediaMetadata({ title: "Morf test video", artist: "Morf checks" });
    });
    for (const file of ["policy.js", "content.js"]) await page.addScriptTag({ path: path.join(root, "browser-extension", file) });
    check(await page.evaluate(() => "documentPictureInPicture" in window), "Chromium exposes Document PiP in the isolated HTTPS fixture");
    await page.evaluate(token => {
      document.querySelector("#start").onclick = () => { window.__opened = window.__morfPip.open({ session: token, seekSeconds: 10, advanced: true }); };
      document.querySelector("#native").onclick = () => window.__deliver({ channel: "morf-pip", session: token, type: "open" });
    }, token);
    const pipEvent = context.waitForEvent("page", { timeout: 7000 });
    pipEvent.catch(() => {});
    await page.getByRole("button", { name: "PiP aç", exact: true }).click();
    console.log("OPEN RESULT", await page.evaluate(() => window.__opened));
    const pip = await pipEvent;
    check((await page.evaluate(() => window.__opened)).ok, "A trusted browser click opens the real native Document PiP window");
    await pip.waitForSelector("video");
    check(await pip.locator("video").evaluate(video => video.videoWidth === 640 && video.videoHeight === 360 && !video.paused), "The original decoded video keeps playing inside PiP");
    check(await page.locator("video").count() === 0, "PiP moves the original video without creating a second decoder element");
    check(await pip.getByRole("button", { name: "10 saniye geri", exact: true }).count() === 1
      && await pip.getByRole("button", { name: "10 saniye ileri", exact: true }).count() === 1, "PiP shows the configured ten-second controls");
    await pip.setViewportSize({ width: 300, height: 200 });
    await pip.locator("main").hover();
    await pip.screenshot({ path: path.join(output, "pip-300.png") });
    await pip.setViewportSize({ width: 216, height: 122 });
    await pip.locator("main").hover();
    const compactLayout = await pip.locator("main").evaluate(player => {
      const window = player.getBoundingClientRect();
      const play = player.querySelector(".play").getBoundingClientRect();
      const timeline = player.querySelector("input").getBoundingClientRect();
      const header = player.querySelector(".top").getBoundingClientRect();
      const time = player.querySelector(".time").getBoundingClientRect();
      return Math.abs(play.left + play.width / 2 - window.width / 2) < 1
        && header.bottom <= play.top && play.bottom <= timeline.top && time.bottom <= window.bottom;
    });
    check(compactLayout, "Small PiP keeps playback centered and separates the header, timeline and time readout");
    await pip.screenshot({ path: path.join(output, "pip-216.png") });
    await pip.setViewportSize({ width: 300, height: 200 });
    await pip.locator("main").hover();
    check(await pip.locator("video").evaluate(video => getComputedStyle(video).objectFit === "contain"), "Resizing preserves the displayed video's proportions");
    check(await pip.getByRole("button", { name: "Sonraki video", exact: true }).isHidden(), "Unsupported next/previous actions are hidden");
    await page.evaluate(token => window.__deliver({ channel: "morf-pip", session: "f".repeat(32), type: "dispose" }), token);
    check(!pip.isClosed(), "A stale or forged session token cannot close the current PiP video");
    await page.evaluate(token => window.__deliver({ channel: "morf-pip", session: token, type: "config", seekSeconds: 5 }), token);
    check(await pip.getByRole("button", { name: "5 saniye ileri", exact: true }).count() === 1, "An explicit setting change updates active controls without restarting playback");
    await pip.getByRole("button", { name: "Duraklat ve PiP'yi kapat", exact: true }).click();
    await pip.waitForEvent("close").catch(() => {});
    check(await page.locator("video").evaluate(video => video.paused && video.getAttribute("style") === "width:640px;height:360px"), "Closing pauses and restores the original video and its source style");
    check(await page.evaluate(() => window.__listenerCount() === 0), "Closing removes the selected video's runtime listener");
    check(await page.evaluate(() => ![...document.querySelector("#source").childNodes].some(node => node.nodeType === 8)), "Closing removes the temporary source placeholder");

    const reopen = context.waitForEvent("page"); await page.getByRole("button", { name: "PiP aç", exact: true }).click(); const second = await reopen;
    await second.waitForSelector("video"); await second.locator("main").hover();
    await second.getByRole("button", { name: "Oynat", exact: true }).click();
    await second.getByRole("button", { name: "Kaynak sekmeye dön", exact: true }).click();
    check(await page.locator("video").evaluate(video => !video.paused), "Return to tab restores the video while keeping playback running");
    check(await page.evaluate(() => window.__listenerCount() === 0), "Return to tab releases the PiP session resources");

    const lastOpen = context.waitForEvent("page"); await page.getByRole("button", { name: "PiP aç", exact: true }).click(); const last = await lastOpen;
    await last.waitForSelector("video");
    await page.evaluate(token => window.__deliver({ channel: "morf-pip", session: token, type: "dispose" }), token);
    await last.waitForEvent("close").catch(() => {});
    check(await page.locator("video").evaluate(video => video.paused) && await page.evaluate(() => window.__listenerCount() === 0), "Disabling/disconnecting Morf pauses PiP and removes its browser resources");
    const mediaBytes = await page.evaluate(async () => Array.from(new Uint8Array(await (await fetch(document.querySelector("video").src)).arrayBuffer())));
    // The standard path must use video PiP and leave the original DOM in its tab.
    await page.evaluate(token => {
      document.querySelector("#start").onclick = () => { window.__opened = window.__morfPip.open({ session: token, seekSeconds: 10 }); };
    }, token);
    await page.getByRole("button", { name: "PiP aç", exact: true }).click();
    check((await page.evaluate(() => window.__opened)).ok && await page.evaluate(() => document.pictureInPictureElement === document.querySelector("video")),
      "The default opens standard video PiP without a Document PiP origin header");
    check(await page.locator("#source video").count() === 1 && await page.evaluate(() => !window.documentPictureInPicture.window),
      "Standard PiP retains the source player and creates no custom document window");
    await page.evaluate(token => window.__deliver({ channel: "morf-pip", session: token, type: "dispose" }), token);
    await page.waitForFunction(() => !document.pictureInPictureElement);
    check(await page.locator("video").evaluate(video => video.paused) && await page.evaluate(() => window.__listenerCount() === 0),
      "Standard PiP disconnect closes the window, pauses and releases listeners");
    check(!await page.evaluate(() => window.__morfPip.openInline()), "An unlinked player cannot bypass the host's PiP setting");
    await page.goto("https://www.youtube.com/watch?v=main1234567");
    await page.evaluate(async ({ token, data }) => {
      const listeners = new Set(); window.__next = 0; window.__previous = 0;
      window.chrome.runtime = { sendMessage: async () => {}, onMessage: { addListener: fn => listeners.add(fn), removeListener: fn => listeners.delete(fn) } };
      const source = document.querySelector("#source"), video = document.querySelector("video");
      const player = document.createElement("div"); player.id = "movie_player";
      player.getVideoData = () => ({ video_id: "main1234567", title: "Local YouTube adapter fixture", author: "Morf checks" });
      const slot = document.createElement("div"); slot.className = "html5-video-container";
      slot.append(video); player.append(slot);
      for (const direction of ["next", "previous"]) {
        const button = document.createElement("button"); button.className = direction === "next" ? "ytp-next-button" : "ytp-prev-button";
        button.textContent = direction; button.onclick = () => { if (direction === "next") window.__next++; else window.__previous++; };
        player.append(button);
      }
      source.append(player); video.src = "data:video/webm;base64," + data;
      await new Promise(resolve => { video.onloadeddata = resolve; }); await video.play();
      navigator.mediaSession.metadata = new MediaMetadata({ title: "Local YouTube adapter fixture" });
    }, { token, data: Buffer.from(mediaBytes).toString("base64") });
    for (const file of ["youtube-media.js", "policy.js", "content.js"]) await page.addScriptTag({ path: path.join(root, "browser-extension", file) });
    check(await page.evaluate(() => {
      const main = new MediaMetadata({ title: "Local YouTube adapter fixture", artist: "Morf checks", artwork: [{ src: "https://i.ytimg.com/vi/main1234567/hqdefault.jpg" }] });
      navigator.mediaSession.metadata = main;
      navigator.mediaSession.metadata = new MediaMetadata({ title: "Recommended hover preview", artwork: [{ src: "https://example.invalid/wrong.jpg" }] });
      return navigator.mediaSession.metadata === main && navigator.mediaSession.metadata.artwork[0].src.includes("main1234567");
    }), "Real MediaSession rejects a recommendation's title/artwork and retains the main video object");
    await page.evaluate(token => { document.querySelector("#start").onclick = () => { window.__opened = window.__morfPip.open({ session: token, seekSeconds: 10, advanced: true }); }; }, token);
    const adapterEvent = context.waitForEvent("page");
    await page.getByRole("button", { name: "PiP aç", exact: true }).click(); const adapter = await adapterEvent;
    await adapter.locator("main").hover();
    await adapter.getByRole("button", { name: "Sonraki video", exact: true }).click();
    await adapter.getByRole("button", { name: "Önceki video", exact: true }).click();
    check(await page.evaluate(() => window.__next === 1 && window.__previous === 1), "Available previous/next actions invoke the original controller buttons inside the transferred player");
    await adapter.evaluate(() => {
      const player = document.querySelector("#movie_player");
      const ads = document.createElement("div"); ads.className = "video-ads";
      const button = document.createElement("button"); button.className = "ytp-skip-ad-button";
      button.hidden = true; button.onclick = () => player.classList.remove("ad-showing");
      ads.append(button); player.append(ads); player.classList.add("ad-showing");
    });
    const skipAd = adapter.getByRole("button", { name: "Reklamı geç", exact: true });
    check(await skipAd.isHidden(), "An unavailable ad skip is hidden");
    await adapter.locator("button.ytp-skip-ad-button").evaluate(button => button.hidden = false);
    await skipAd.click();
    check(!await adapter.locator("#movie_player").evaluate(player => player.classList.contains("ad-showing")), "Skip invokes the original ad control when it becomes available");
    await adapter.getByRole("button", { name: "Duraklat ve PiP'yi kapat", exact: true }).click();
    check(await page.locator("#movie_player video").evaluate(video => video.paused), "The bounded YouTube player subtree is restored with its original video");
    check(errors.length === 0, "No uncaught browser errors: " + errors.join("; "));
    console.log(JSON.stringify({ passed, browser: browser.version(), fixture: "locally generated VP8 video; Native Messaging simulated", headed: process.env.MORF_HEADED === "1" }));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
