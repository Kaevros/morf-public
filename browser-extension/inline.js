/* The automatic controls are limited to the HTTPS players listed in the manifest. */
(() => {
  "use strict";
  const api = globalThis.browser || chrome;
  const player = globalThis.__morfPip;
  const youtube = location.hostname === "www.youtube.com";
  const button = document.createElement("button");
  button.type = "button";
  button.title = "Morf · Pencere içinde video";
  button.setAttribute("aria-label", button.title);
  const ns = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(ns, "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  svg.style.cssText = "width:22px;height:22px;pointer-events:none";
  const path = document.createElementNS(ns, "path");
  path.setAttribute("d", "M3 5h18v14H3z M12 11h6v5h-6z");
  path.setAttribute("stroke", "currentColor");
  path.setAttribute("stroke-width", "1.6");
  path.setAttribute("fill", "none");
  svg.append(path);
  button.append(svg);
  button.className = youtube ? "ytp-button" : "morf-pip-button";
  button.style.cssText = youtube ? "display:inline-grid;place-items:center;vertical-align:top;color:white" :
    "display:inline-grid;place-items:center;width:40px;height:40px;border:0;background:transparent;color:white;cursor:pointer";
  player.bindInline(button);
  let linking = false;

  function video() {
    return youtube ? document.querySelector("#movie_player video") : document.querySelector("video");
  }
  function mount() {
    const media = video();
    const controls = youtube ? media?.closest("#movie_player")?.querySelector(".ytp-right-controls") :
      document.querySelector('[data-uia="controls-standard"]');
    if (!controls || !media || media.disablePictureInPicture || !media.videoWidth) return null;
    if (button.parentNode !== controls) {
      const fullscreen = controls.querySelector(".ytp-fullscreen-button");
      controls.insertBefore(button, fullscreen?.parentNode === controls ? fullscreen : null);
      player.bindInline(button);
    }
    button.title = "Morf · Pencere içinde video";
    return media;
  }
  function link() {
    const media = mount();
    if (media && !media.paused && document.visibilityState === "visible" && !linking) {
      linking = true;
      void api.runtime.sendMessage({ channel: "morf-pip", type: "inline-ready" }).catch(() => {}).finally(() => { linking = false; });
    }
  }
  button.addEventListener("click", async event => {
    if (!event.isTrusted) return;
    event.stopPropagation();
    try {
      if (await player.openInline()) {
        void api.runtime.sendMessage({ channel: "morf-pip", type: "inline-click" }).catch(() => {});
        return;
      }
      const linked = await api.runtime.sendMessage({ channel: "morf-pip", type: "inline-click" });
      if (linked?.ok && await player.openInline()) return;
      button.title = "Morf açık olmalı ve PiP ayarı etkin olmalı.";
    } catch {
      button.title = "PiP açılamadı. Yeniden tıklayın.";
    }
  });
  api.runtime.onMessage.addListener((message, sender, respond) => {
    if (sender.id !== api.runtime.id || message?.channel !== "morf-pip" || message.type !== "prepare") return;
    void player.open({ ...message, prepare: true }).then(respond, () => respond({ ok: false }));
    return true;
  });
  for (const event of ["loadedmetadata", "play", "visibilitychange", "yt-navigate-finish", "yt-page-data-updated"])
    document.addEventListener(event, event => {
      if (youtube && event?.type === "play" && !event.target?.closest?.("#movie_player")) {
        return;
      }
      link();
    }, true);
  // Some players create their controls only when first hovered.
  document.addEventListener("pointerover", event => {
    if (!button.isConnected && event.target.closest?.("#movie_player, [data-uia='video-canvas']")) link();
  });
  link();
})();
