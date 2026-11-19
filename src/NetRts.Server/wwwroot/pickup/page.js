// What both pick-up pages (the phone and the big screen) need from the site: their stylesheet, and the
// page to themselves (no site padding; the big screen also hides the site's top bar and footer).

export function loadPickupStyles() {
  if (document.querySelector('link[data-pickup-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = new URL('./pickup.css', import.meta.url).href;
  link.dataset.pickupCss = '';
  document.head.append(link);
}

/** Marks main (and, for the big screen, the whole document) as a pick-up page; returns the undo. */
export function takePage(main, { full = false } = {}) {
  loadPickupStyles();
  main.classList.add('pk-main');
  if (full) document.documentElement.classList.add('pk-full');
  return () => {
    main.classList.remove('pk-main');
    document.documentElement.classList.remove('pk-full');
  };
}
