// Colour theme toggle: Auto (follow the system) → Light → Dark. The choice is kept in localStorage;
// index.html applies it before first paint so the page never flashes the wrong theme.
const KEY = 'netrts-theme';
const ORDER = ['auto', 'light', 'dark'];
const LABEL = { auto: 'Auto', light: 'Light', dark: 'Dark' };

const button = document.getElementById('theme-toggle');
const label = document.getElementById('theme-label');

function read() {
  try {
    const value = localStorage.getItem(KEY);
    return ORDER.includes(value) ? value : 'auto';
  } catch {
    return 'auto';
  }
}

function apply(choice) {
  if (choice === 'auto') delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = choice;
  label.textContent = LABEL[choice];
  button.setAttribute('aria-label', `Colour theme: ${LABEL[choice].toLowerCase()}. Click to change.`);
  window.dispatchEvent(new Event('netrts-theme'));
}

let current = read();

button.addEventListener('click', () => {
  current = ORDER[(ORDER.indexOf(current) + 1) % ORDER.length];
  try { localStorage.setItem(KEY, current); } catch { /* storage blocked: still switch for this visit */ }
  apply(current);
});

apply(current);

// Brand toggle: the original NetRts look or the Snow College theme. Switching reloads the page so every
// view re-renders in one vocabulary.
const brandButton = document.getElementById('brand-toggle');
const snow = document.documentElement.dataset.brand === 'snow';
brandButton.setAttribute('aria-label', snow
  ? 'Site theme: Snow College. Click for the original theme.'
  : 'Site theme: original. Click for the Snow College theme.');
brandButton.addEventListener('click', () => {
  try { localStorage.setItem('netrts-brand', snow ? 'classic' : 'snow'); } catch { /* storage blocked */ }
  location.reload();
});
