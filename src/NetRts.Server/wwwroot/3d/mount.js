// Starting the 3D view on a page (the spectator's 3D mode, the Block Bot cockpit). three.js is big, so
// view.js and audio.js load only when a page asks for them; this module itself imports neither.
import { SNOW } from '../lore.js';

const load = () => Promise.all([import('./view.js'), import('./audio.js')]);

/** Fetches the 3D view's code ahead of time (while the reader watches in 2D, say). */
export function preload3d() { load().catch(() => {}); }

/**
 * A View3D (with its sound) filling box. names: () => Map(slot -> name). Other options go to View3D, and
 * onReady (called once the valley is built) is set on it. scope: a mountScope; if the page has gone by the
 * time the code has loaded, nothing is made and the promise never settles (see mountScope.wait). Throws
 * when the code can't load or WebGL can't start.
 *
 * Returns { view3d, audio, dispose }; dispose stops the sound and destroys the view.
 */
export async function mount3d(box, { names, scope = null, onReady = null, ...opts } = {}) {
  const [{ View3D }, { GameAudio }] = await (scope ? scope.wait(load()) : load());
  const audio = new GameAudio();
  let view3d;
  try {
    view3d = new View3D(box, { badger: SNOW, names, audio, ...opts });
  } catch (e) {
    audio.dispose?.();
    throw e;
  }
  box.view3d = view3d;   // handy from the dev tools console
  if (onReady) view3d.onReady = onReady;
  let disposed = false;
  return {
    view3d,
    audio,
    dispose() {
      if (disposed) return;
      disposed = true;
      audio.setActive(false);
      audio.dispose?.();
      view3d.destroy();
      if (box.view3d === view3d) delete box.view3d;
    },
  };
}
