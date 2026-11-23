/* Runs in YouTube's MAIN world so preview metadata is rejected before Windows sees it.
 * No extension APIs, messages, timers, network requests or playback commands. */
(() => {
  "use strict";
  if (globalThis.__morfYoutubeMedia || !globalThis.MediaSession || !globalThis.MediaMetadata) return;
  const descriptor = Object.getOwnPropertyDescriptor(MediaSession.prototype, "metadata");
  if (!descriptor?.get || !descriptor.set || !descriptor.configurable) return;
  let accepted = null, acceptedId = null, watchedPlayer = null;
  function mainVideo() {
    if (location.pathname !== "/watch") return null;
    try {
      const id = new URL(location.href).searchParams.get("v");
      const player = document.getElementById("movie_player") || watchedPlayer;
      if (player && player.isConnected !== false) watchedPlayer = player;
      else return null;
      const data = player?.getVideoData?.();
      // During navigation, leave metadata alone until the new player agrees with the URL.
      if (!id || !/^[\w-]{11}$/.test(id) || data?.video_id !== id || !data.title
        || !player.querySelector("video")?.readyState || player.classList?.contains("ad-showing")) return null;
      return { id, title: data.title, artist: data.author || "" };
    } catch { return null; }
  }
  Object.defineProperty(MediaSession.prototype, "metadata", {
    ...descriptor,
    set(value) {
      const main = this === navigator.mediaSession && value && mainVideo();
      if (!main) { accepted = null; acceptedId = null; descriptor.set.call(this, value); return; }
      if (value.title !== main.title) {
        // Keeping the already accepted object avoids spurious track changes/peek expansion.
        if (acceptedId === main.id && accepted && accepted.title === main.title
          && descriptor.get.call(this) === accepted) return;
        value = new MediaMetadata({ title: main.title, artist: main.artist,
          artwork: [{ src: "https://i.ytimg.com/vi/" + main.id + "/hqdefault.jpg", sizes: "480x360", type: "image/jpeg" }] });
      }
      descriptor.set.call(this, value);
      accepted = value; acceptedId = main.id;
    }
  });
  globalThis.__morfYoutubeMedia = true;
})();
