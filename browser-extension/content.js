/* Injected into a single user-selected HTTPS tab, in the extension's isolated world. */
(() => {
  "use strict";
  if (globalThis.__morfPip) return;
  const api = globalThis.browser || chrome;
  const policy = MorfPipPolicy;
  let active = null;
  let inlineButton = null;

  async function markButton(button, session, suffix = "") {
    const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(session));
    if (active?.session === session) button.id = "morf-pip-" + Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, "0")).join("") + suffix;
  }

  function send(state, type, extra = {}) {
    if (!state.disposed) void api.runtime.sendMessage({ channel: "morf-pip", session: state.session, type, ...extra }).catch(() => dispose(state, true, false));
  }
  function metadata(state) {
    let data = navigator.mediaSession?.metadata;
    let title = data?.title;
    let artist = data?.artist;
    if (state.youtube) {
      const moviePlayer = document.getElementById("movie_player");
      const videoData = moviePlayer?.getVideoData?.();
      if (videoData?.title) {
        title = videoData.title;
        artist = videoData.author || artist;
      }
    }
    return {
      title: policy.text(title || document.title.replace(/\s*[-|]\s*(YouTube|Netflix)\s*$/i, ""), 512),
      artist: policy.text(artist, 256), open: !!state.pip && !state.pip.closed || document.pictureInPictureElement === state.video,
      inline: !!inlineButton?.id && inlineButton.isConnected
    };
  }
  function publish(state) {
    if (active === state && !state.disposed) send(state, "status", metadata(state));
  }
  function chooseVideo() {
    const youtube = location.hostname === "youtube.com" || location.hostname.endsWith(".youtube.com");
    if (youtube) {
      const main = document.querySelector("#movie_player video");
      if (main && !main.disablePictureInPicture) return main;
    }
    return [...document.querySelectorAll("video")]
      .filter(video => video.readyState > 0 && video.videoWidth > 0 && video.videoHeight > 0 && !video.disablePictureInPicture
        && (!youtube || video.closest("#movie_player")))
      .sort((a, b) => Number(a.paused) - Number(b.paused)
        || b.getBoundingClientRect().width * b.getBoundingClientRect().height - a.getBoundingClientRect().width * a.getBoundingClientRect().height)[0];
  }
  function seekRanges(video) {
    const ranges = [];
    for (let i = 0; i < video.seekable.length; i++) ranges.push([video.seekable.start(i), video.seekable.end(i)]);
    return ranges;
  }
  function seek(state, offset) {
    const target = policy.seekTarget(state.video.currentTime, offset, seekRanges(state.video));
    if (target !== null) {
      try { state.video.currentTime = target; } catch { showFeedback(state, "Bu videoda sarma kullanılamıyor."); }
    }
  }
  function siteButton(state, direction) {
    // Only known, packaged adapters. Never selectors or code received from the page/native host.
    const host = location.hostname;
    let selector;
    if (host === "youtube.com" || host.endsWith(".youtube.com")) selector = direction === "next" ? ".ytp-next-button" : ".ytp-prev-button";
    else if (host === "netflix.com" || host.endsWith(".netflix.com")) selector = direction === "next" ? 'button[data-uia="next-episode-seamless-button"]' : null;
    const button = selector && (state.transfer.querySelector?.(selector) || document.querySelector(selector));
    const transferred = button && state.pip && state.transfer.ownerDocument === state.pip.document && state.transfer.contains(button);
    const visible = transferred ? !button.closest('[hidden],[aria-hidden="true"]') && button.style.display !== "none"
      && (button.tagName !== "A" || isWatchLink(button.href)) : button?.getClientRects().length;
    return button && !button.disabled && button.getAttribute("aria-disabled") !== "true" && visible ? button : null;
  }
  function isWatchLink(href) {
    if (!href) return false;
    try { const url = new URL(href); return url.origin === location.origin && url.pathname === "/watch" && !!url.searchParams.get("v"); }
    catch { return false; }
  }
  function youtubeAction(state, direction) {
    if (!state.youtube || state.transfer.classList.contains("ad-showing")) return null;
    const current = document.querySelector("ytd-playlist-panel-video-renderer[selected]");
    const neighbor = direction === "next" ? current?.nextElementSibling : current?.previousElementSibling;
    const link = neighbor?.matches("ytd-playlist-panel-video-renderer") && neighbor.querySelector("a#wc-endpoint, a[href*='watch?']");
    if (link && isWatchLink(link.href)) return () => link.click();
    if (direction === "previous") {
      const navigation = window.navigation;
      const index = navigation?.currentEntry?.index;
      const entry = index > 0 && navigation.entries()[index - 1];
      if (entry && isWatchLink(entry.url) && new URL(entry.url).searchParams.get("v") !== new URL(location.href).searchParams.get("v"))
        return () => navigation.traverseTo(entry.key).finished.catch(() => showFeedback(state, "Önceki video açılamadı."));
    } else if (!current) {
      const suggested = document.querySelector("#related ytd-compact-video-renderer a#thumbnail, #related yt-lockup-view-model a[href*='/watch?']");
      if (suggested && isWatchLink(suggested.href)) return () => suggested.click();
    }
    return null;
  }
  function videoAction(state, direction) {
    if (state.youtube && state.transfer.classList.contains("ad-showing")) return null;
    const target = siteButton(state, direction);
    return target ? () => target.click() : youtubeAction(state, direction);
  }
  function skipAdTarget(state) {
    if (!state.youtube || !state.transfer.classList.contains("ad-showing")) return null;
    for (const button of state.transfer.querySelectorAll("button.ytp-skip-ad-button,button.ytp-ad-skip-button-modern,button.ytp-ad-skip-button")) {
      if (button.disabled || button.getAttribute("aria-disabled") === "true") continue;
      let node = button;
      while (node && node !== state.transfer && !node.hidden && node.getAttribute("aria-hidden") !== "true"
        && node.style.display !== "none" && node.style.visibility !== "hidden") node = node.parentElement;
      if (node === state.transfer) return button;
    }
    return null;
  }
  function updateYoutube(state) {
    if (state.disposed || !state.controls) return;
    state.controls.skipAd.hidden = !skipAdTarget(state);
    const ads = state.transfer.classList.contains("ad-showing") && state.transfer.querySelector(".video-ads, .ytp-ad-module");
    if (state.adContainer === ads) return;
    state.adObserver?.disconnect();
    state.adContainer = ads;
    if (ads) {
      state.adObserver = new MutationObserver(() => { state.controls.skipAd.hidden = !skipAdTarget(state); });
      state.adObserver.observe(ads, { childList: true, subtree: true, attributes: true, attributeFilter: ["hidden", "style", "class", "disabled", "aria-hidden", "aria-disabled"] });
    }
  }
  function showFeedback(state, message) {
    if (state.feedback) state.feedback.textContent = message;
  }
  function restoreVideo(state) {
    // Restore the original element and inline style; do not reload URLs, copy the stream or play twice.
    if (state.marker?.parentNode) state.marker.parentNode.replaceChild(state.transfer, state.marker);
    else if (state.parent?.isConnected && state.transfer.ownerDocument !== document) state.parent.append(state.transfer);
    if (state.originalStyle === null) state.video.removeAttribute("style");
    else state.video.setAttribute("style", state.originalStyle);
    state.marker = null;
  }
  function dispose(state, pause = true, notify = true) {
    if (!state || state.disposed) return;
    if (pause && !state.prepared) state.video.pause();
    // Notify before marking disposed, and then remove every observer/listener/DOM affordance.
    if (notify) send(state, "closed");
    state.disposed = true;
    state.abort.abort();
    state.observer?.disconnect();
    state.playerObserver?.disconnect();
    state.adObserver?.disconnect();
    api.runtime.onMessage.removeListener(state.messageListener);
    state.invitation?.remove();
    if (state.marker) restoreVideo(state);
    if (state.pip && !state.pip.closed) state.pip.close();
    if (document.pictureInPictureElement === state.video) void document.exitPictureInPicture().catch(() => {});
    if (active === state) { active = null; inlineButton?.removeAttribute("id"); }
  }
  function returnToTab(state) {
    send(state, "activation"); // The worker uses trusted tabs/windows APIs, not a page URL.
    dispose(state, false);
  }
  function updateProgress(state) {
    if (!state.controls || state.disposed || state.scrubbing) return;
    const { progress, time } = state.controls;
    const ranges = seekRanges(state.video);
    const start = ranges[0]?.[0], end = ranges.at(-1)?.[1];
    progress.hidden = !ranges.length || !Number.isFinite(end) || end <= start;
    if (!progress.hidden) {
      if (progress.min !== String(start)) progress.min = String(start);
      if (progress.max !== String(end)) progress.max = String(end);
      if (progress.value !== String(state.video.currentTime)) progress.value = String(state.video.currentTime);
      paintProgress(progress);
      const value = Math.floor(state.video.currentTime) + " saniye";
      if (progress.getAttribute("aria-valuetext") !== value) progress.setAttribute("aria-valuetext", value);
    }
    const duration = state.video.duration;
    const elapsed = formatTime(state.video.currentTime);
    const label = Number.isFinite(duration) ? elapsed + " / " + formatTime(duration) : elapsed;
    if (time.textContent !== label) time.textContent = label;
  }
  function formatTime(seconds) {
    const total = Math.max(0, Math.floor(Number.isFinite(seconds) ? seconds : 0));
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor(total / 60) % 60;
    const remainder = String(total % 60).padStart(2, "0");
    return hours ? hours + ":" + String(minutes).padStart(2, "0") + ":" + remainder : minutes + ":" + remainder;
  }
  function paintProgress(progress) {
    const fraction = (progress.valueAsNumber - Number(progress.min)) / (Number(progress.max) - Number(progress.min));
    const fill = (Math.max(0, Math.min(1, fraction)) * 100).toFixed(1) + "%";
    if (progress.style.getPropertyValue("--filled") !== fill) progress.style.setProperty("--filled", fill);
  }
  function labelButton(button, label) {
    button.setAttribute("aria-label", label);
    button.title = label;
  }
  function icon(doc, outline) {
    const namespace = "http://www.w3.org/2000/svg";
    const svg = doc.createElementNS(namespace, "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("aria-hidden", "true");
    svg.setAttribute("focusable", "false");
    const path = doc.createElementNS(namespace, "path");
    path.setAttribute("d", outline);
    svg.append(path);
    return svg;
  }
  function seekIcon(doc, forward) {
    const svg = icon(doc, forward
      ? "M12 4a8 8 0 1 0 7.5 5.2M12 1l3 3-3 3"
      : "M12 4a8 8 0 1 1-7.5 5.2M12 1 9 4l3 3");
    const number = doc.createElementNS(svg.namespaceURI, "text");
    number.setAttribute("x", "12");
    number.setAttribute("y", "16");
    number.setAttribute("text-anchor", "middle");
    svg.append(number);
    return { svg, number };
  }
  function updateControls(state) {
    if (!state.controls || state.disposed) return;
    const c = state.controls;
    const paused = state.video.paused;
    c.play.dataset.paused = String(paused);
    labelButton(c.play, paused ? "Oynat" : "Duraklat");
    c.backwardNumber.textContent = c.forwardNumber.textContent = String(state.seconds);
    labelButton(c.backward, state.seconds + " saniye geri");
    labelButton(c.forward, state.seconds + " saniye ileri");
    const ranges = seekRanges(state.video);
    c.backward.hidden = c.forward.hidden = !ranges.length;
    updateProgress(state);
    for (const direction of ["previous", "next"]) {
      const available = !!videoAction(state, direction);
      c[direction].hidden = !state.youtube && !available;
      c[direction].disabled = !available;
    }
    if (state.youtube) updateYoutube(state);
    const info = metadata(state);
    if (state.pip.document.title !== "Morf · " + info.title) state.pip.document.title = "Morf · " + info.title;
    if (c.title.textContent !== info.title) c.title.textContent = info.title;
  }
  function buildPlayer(state) {
    const doc = state.pip.document;
    doc.documentElement.lang = "tr";
    const style = doc.createElement("style");
    style.textContent = `
      :root {
        color-scheme: dark; font: 13px "Segoe UI", system-ui, sans-serif;
        --canvas: #08090b; --ink: #faf9ff; --accent: #d0c8ff; --focus: #b3a5ff;
        background: var(--canvas); color: var(--ink);
      }
      * { box-sizing: border-box; } body { margin: 0; width: 100vw; height: 100vh; overflow: hidden; }
      .player { width: 100%; height: 100%; position: relative; background: var(--canvas); }
      .video { width: 100%; height: 100%; display: grid; place-items: center; }
      .video > #movie_player, .video .html5-video-container { width: 100% !important; height: 100% !important; position: relative !important; }
      .video > #movie_player > :not(.html5-video-container) { display: none !important; }
      .video video { width: 100% !important; height: 100% !important; max-width: none !important; max-height: none !important; object-fit: contain !important; position: static !important; margin: 0 !important; transform: none !important; }
      .overlay {
        position: absolute; inset: 0; opacity: 0; pointer-events: none; transition: opacity 150ms ease;
        background: linear-gradient(180deg, #000b, #0001 29%, #0002 65%, #000d);
        --inset: clamp(8px, 3vw, 22px); --key: clamp(27px, 9vw, 44px);
        --primary: clamp(38px, 13vw, 60px); --gap: clamp(2px, 1.6vw, 13px);
      }
      .player:hover .overlay, .player:has(:focus-visible) .overlay { opacity: 1; pointer-events: auto; }
      .top, .bottom { position: absolute; inset-inline: var(--inset); }
      .top { top: var(--inset); display: flex; align-items: center; gap: 3px; }
      .title {
        min-width: 0; padding-right: 6px; overflow: hidden; white-space: nowrap; text-overflow: ellipsis;
        flex: 1; font-weight: 600; text-shadow: 0 1px 3px #000;
      }
      .transport {
        position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%);
        display: grid; align-items: center; justify-items: center; gap: var(--gap);
        grid-template-columns: repeat(2, var(--key)) var(--primary) repeat(2, var(--key));
      }
      .previous { grid-column: 1; } .backward { grid-column: 2; } .play { grid-column: 3; }
      .forward { grid-column: 4; } .next { grid-column: 5; }
      .transport button { grid-row: 1; }
      button {
        appearance: none; border: 0; border-radius: 50%; padding: 0; display: grid; place-items: center;
        width: var(--key); height: var(--key); background: #100f176e; color: inherit; cursor: pointer;
        -webkit-tap-highlight-color: transparent;
      }
      button:hover { background: #232132e0; }
      button:disabled { opacity: .36; cursor: default; }
      .top .skip-ad { width: auto; border-radius: 12px; padding: 0 8px; font: inherit; background: #272432; }
      button:focus-visible, input:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
      button svg {
        width: 62%; max-width: 25px; fill: none; stroke: currentColor; stroke-width: 1.75;
        stroke-linecap: round; stroke-linejoin: round; pointer-events: none;
      }
      .top button { width: 32px; height: 32px; flex: none; }
      .top svg { max-width: 17px; }
      .play {
        width: var(--primary); height: var(--primary); background: #eeebff; color: #1d1932;
        box-shadow: 0 2px 12px #0005;
      }
      .play:hover { background: #fff; }
      .play svg { max-width: 27px; fill: currentColor; stroke: none; }
      .play[data-paused="true"] svg:last-child, .play[data-paused="false"] svg:first-child { display: none; }
      .seek text { fill: currentColor; stroke: none; font: 700 8px "Segoe UI", system-ui, sans-serif; }
      .bottom { bottom: clamp(6px, 2.4vh, 16px); }
      .time { display: block; font-size: 11px; font-variant-numeric: tabular-nums; text-shadow: 0 1px 3px #000; }
      input[type=range] {
        appearance: none; display: block; width: 100%; height: 18px; padding: 0; margin: 0 0 2px;
        border: 0; border-radius: 3px; background: transparent; cursor: pointer;
      }
      input[type=range]::-webkit-slider-runnable-track {
        height: 3px; border-radius: 2px;
        background: linear-gradient(to right, var(--accent) var(--filled, 0%), #ffffff4d var(--filled, 0%));
      }
      input[type=range]::-webkit-slider-thumb {
        appearance: none; width: 4px; height: 11px; margin-top: -4px; border: 0; border-radius: 2px;
        background: var(--accent);
      }
      .feedback { font-size: 11px; margin: 0 0 3px; text-shadow: 0 1px 3px #000; }
      .feedback:empty { display: none; }
      [hidden] { display: none !important; }
      @media (max-height: 126px) {
        .overlay { --key: 25px; --primary: 34px; --inset: 7px; }
        .top button { width: 26px; height: 26px; }
        .transport { top: 49%; }
        .bottom { bottom: 3px; } input[type=range] { height: 14px; margin-bottom: 0; }
      }
      @media (prefers-reduced-motion: reduce) { .overlay { transition: none; } }
    `;
    doc.head.append(style);
    const root = doc.createElement("main"); root.className = "player"; root.setAttribute("aria-label", "Morf video oynatıcı");
    const slot = doc.createElement("div"); slot.className = "video";
    const top = doc.createElement("div"); top.className = "top";
    const title = doc.createElement("span"); title.className = "title";
    const button = (label, className, action, ...icons) => {
      const element = doc.createElement("button"); element.type = "button";
      element.className = className; labelButton(element, label); element.append(...icons);
      element.addEventListener("click", action, { signal: state.abort.signal }); return element;
    };
    const skipAd = button("Reklamı geç", "skip-ad", () => skipAdTarget(state)?.click(), "Reklamı geç");
    skipAd.hidden = true;
    top.append(title, skipAd, button("Kaynak sekmeye dön", "return", () => returnToTab(state),
      icon(doc, "M12 5H5v14h14v-7M14 5h5v5m0-5-8 8")),
    button("Duraklat ve PiP'yi kapat", "close", () => dispose(state), icon(doc, "m6 6 12 12M18 6 6 18")));
    const bottom = doc.createElement("div"); bottom.className = "bottom";
    const feedback = doc.createElement("p"); feedback.className = "feedback"; feedback.setAttribute("role", "status");
    const progress = doc.createElement("input"); progress.type = "range"; progress.step = "0.1"; progress.setAttribute("aria-label", "Video konumu");
    const time = doc.createElement("span"); time.className = "time";
    progress.addEventListener("input", () => {
      state.scrubbing = true;
      paintProgress(progress);
    }, { signal: state.abort.signal });
    progress.addEventListener("change", () => { const target = policy.seekTarget(Number(progress.value), 0, seekRanges(state.video));
      if (target !== null) { try { state.video.currentTime = target; } catch { showFeedback(state, "Bu konuma gidilemiyor."); } }
      state.scrubbing = false; updateControls(state);
    }, { signal: state.abort.signal });
    progress.addEventListener("blur", () => { state.scrubbing = false; }, { signal: state.abort.signal });
    const transport = doc.createElement("div"); transport.className = "transport";
    const skip = direction => { videoAction(state, direction)?.(); updateControls(state); };
    const previous = button("Önceki video", "previous", () => skip("previous"), icon(doc, "M6 5v14m12-14-10 7 10 7Z"));
    const backwardIcon = seekIcon(doc, false);
    const backward = button("Geri sar", "seek backward", () => seek(state, -state.seconds), backwardIcon.svg);
    const play = button("Oynat / duraklat", "play", () => {
      if (state.video.paused) void state.video.play().catch(() => showFeedback(state, "Video oynatılamadı. Kaynak sekmeyi kontrol edin."));
      else state.video.pause();
    }, icon(doc, "M8 4.5v15l12-7.5Z"), icon(doc, "M7 5h3.5v14H7zM13.5 5H17v14h-3.5z"));
    const forwardIcon = seekIcon(doc, true);
    const forward = button("İleri sar", "seek forward", () => seek(state, state.seconds), forwardIcon.svg);
    const next = button("Sonraki video", "next", () => skip("next"), icon(doc, "M18 5v14M6 5l10 7-10 7Z"));
    const overlay = doc.createElement("div"); overlay.className = "overlay";
    transport.append(previous, backward, play, forward, next);
    bottom.append(feedback, progress, time);
    overlay.append(top, transport, bottom);
    root.append(slot, overlay); doc.body.append(root);
    // The page can inspect this DOM. Publish only a one-way window marker.
    void markButton(play, state.session);
    state.controls = { title, progress, time, previous, backward, play, forward, next, skipAd,
      backwardNumber: backwardIcon.number, forwardNumber: forwardIcon.number };
    state.feedback = feedback;
    state.marker = document.createComment("Morf PiP video position");
    state.transfer.before(state.marker);
    slot.append(state.transfer);
    if (state.youtube) {
      state.playerObserver = new MutationObserver(() => updateControls(state));
      state.playerObserver.observe(state.transfer, { attributes: true, attributeFilter: ["class"], childList: true });
      doc.querySelector(".transport").addEventListener("pointerenter", () => updateControls(state), { signal: state.abort.signal });
      for (const event of ["yt-navigate-finish", "yt-page-data-updated"])
        document.addEventListener(event, () => updateControls(state), { signal: state.abort.signal });
    }
    updateControls(state);
    state.pip.addEventListener("pagehide", () => dispose(state), { once: true, signal: state.abort.signal });
  }
  function invite(state) {
    if (state.invitation || state.disposed) return;
    const host = document.createElement("div");
    host.style.cssText = "position:fixed;bottom:24px;right:24px;z-index:2147483647;max-width:calc(100vw - 32px);";
    const shadow = host.attachShadow({ mode: "closed" });
    const style = document.createElement("style");
    style.textContent = ":host{color-scheme:dark}div{font:14px 'Segoe UI',sans-serif;background:#181a20;color:#fff;border:1px solid #454954;border-radius:14px;padding:12px;box-shadow:0 4px 20px #0005;display:flex;align-items:center;gap:10px}button{font:inherit;border:0;border-radius:8px;background:#f4f5f8;color:#15171b;padding:9px 12px;cursor:pointer}button:focus-visible{outline:3px solid #9bb6ff;outline-offset:2px}";
    const panel = document.createElement("div");
    const open = document.createElement("button"); open.type = "button"; open.textContent = "Morf · PiP'yi aç";
    open.addEventListener("click", () => { void openPip(state).catch(() => { open.textContent = "Video açılamadı · Yeniden dene"; }); }, { signal: state.abort.signal });
    const close = document.createElement("button"); close.type = "button"; close.textContent = "×"; close.setAttribute("aria-label", "PiP isteğini iptal et");
    close.addEventListener("click", () => dispose(state, false), { signal: state.abort.signal });
    panel.append(open, close); shadow.append(style, panel); document.documentElement.append(host); state.invitation = host;
  }
  async function openPip(state) {
    if (state.disposed) return;
    if (state.pip && !state.pip.closed) { state.pip.focus(); return; }
    if (document.pictureInPictureElement === state.video) return;
    let pipOpened = false;
    if (state.advanced && "documentPictureInPicture" in window) {
      try {
        const ratio = state.video.videoWidth / state.video.videoHeight || 16 / 9;
        // Browser-managed native resize handles and placement; video keeps its aspect ratio.
        const pip = await documentPictureInPicture.requestWindow({ width: 384, height: Math.round(384 / ratio), disallowReturnToOpener: true });
        if (state.disposed) { pip.close(); return; }
        state.pip = pip;
        state.originalStyle = state.video.getAttribute("style");
        try { buildPlayer(state); pipOpened = true; }
        catch (error) { restoreVideo(state); pip.close(); state.pip = null; throw error; }
      } catch (error) {
        if (!document.pictureInPictureEnabled || state.video.disablePictureInPicture) throw error;
      }
    }
    if (!pipOpened) {
      if (!document.pictureInPictureEnabled || state.video.disablePictureInPicture)
        throw new DOMException("Standart video PiP desteklenmiyor.", "NotSupportedError");
      await state.video.requestPictureInPicture(); // Header-free browser video window; no screen capture.
      if (state.disposed) { if (document.pictureInPictureElement === state.video) await document.exitPictureInPicture(); return; }
      state.video.addEventListener("leavepictureinpicture", () => dispose(state), { once: true, signal: state.abort.signal });
    }
    state.prepared = false;
    state.invitation?.remove(); state.invitation = null; publish(state);
  }
  async function open(config) {
    if (!config || !/^[a-f0-9]{32}$/.test(config.session) || !policy.allowedSite(location.href)) return { ok: false, reason: "site" };
    if (!("documentPictureInPicture" in window) && !document.pictureInPictureEnabled) return { ok: false, reason: "unsupported" };
    const video = chooseVideo();
    if (!video) return { ok: false, reason: "video" };
    dispose(active, false, false);
    // YouTube's controller expects the video to stay inside its original player.
    // Move that bounded player subtree together, retaining its controller/listeners.
    const youtube = location.hostname === "youtube.com" || location.hostname.endsWith(".youtube.com");
    const transfer = youtube ? video.closest("#movie_player") || video : video;
    const state = { session: config.session, video, transfer, youtube, advanced: config.advanced === true, prepared: config.prepare === true, seconds: policy.seekSeconds(config.seekSeconds), abort: new AbortController(),
      parent: transfer.parentNode, originalStyle: video.getAttribute("style"), disposed: false };
    active = state;
    state.messageListener = message => {
      if (message?.channel !== "morf-pip" || message.session !== state.session || state.disposed) return;
      if (message.type === "dispose") dispose(state, true, false);
      else if (message.type === "publish") publish(state);
      else if (message.type === "config") { state.seconds = policy.seekSeconds(message.seekSeconds); state.advanced = message.advanced === true; updateControls(state); }
      else if (message.type === "open") {
        void openPip(state).catch(error => {
          if (error.name === "NotAllowedError") { invite(state); send(state, "activation"); }
          else dispose(state);
        });
      }
    };
    api.runtime.onMessage.addListener(state.messageListener);
    for (const event of ["play", "pause", "loadedmetadata", "durationchange", "emptied", "ended", "seeked"])
      video.addEventListener(event, () => { updateControls(state); publish(state); }, { signal: state.abort.signal });
    video.addEventListener("timeupdate", () => updateProgress(state), { signal: state.abort.signal });
    window.addEventListener("pagehide", () => dispose(state), { once: true, signal: state.abort.signal });
    // Observe only the selected player's original container, only during the explicit session.
    // If a site replaces the video, stop safely rather than controlling a stale element.
    state.observer = new MutationObserver(() => {
      if (state.disposed) return;
      if (!state.video.isConnected && (!state.pip || state.pip.closed)) dispose(state);
      else updateControls(state);
    });
    state.observer.observe(state.parent, { childList: true, subtree: false });
    if (state.prepared && state.transfer !== video)
      state.observer.observe(video.parentNode, { childList: true });
    try {
      if (state.prepared) {
        if (inlineButton) await markButton(inlineButton, state.session, "-inline");
        return { ok: !state.disposed };
      }
      await openPip(state); return { ok: true };
    }
    catch (error) {
      if (error.name === "NotAllowedError") { invite(state); return { ok: true, activation: true }; }
      dispose(state); return { ok: false, reason: "blocked" };
    }
  }
  function bindInline(button) {
    inlineButton = button;
    if (active?.prepared) {
      const state = active;
      void markButton(button, state.session, "-inline").then(() => publish(state));
    }
  }
  async function openInline() {
    if (!active || !active.video?.isConnected) {
      return false; // The host must first confirm that PiP is enabled for this player.
    }
    await openPip(active);
    return true;
  }
  globalThis.__morfPip = Object.freeze({ open, bindInline, openInline });
})();
