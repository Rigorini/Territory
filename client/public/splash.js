/*
 * Bootstrap splash screen.
 *
 * Loaded as a deferred classic script from index.html, so it runs before the Angular
 * bundle is executed but does not block the first paint. It applies the stored theme
 * early (avoiding a flash of the wrong colors), starts waking the server while the
 * JavaScript is still downloading, and exposes window.__territorySplash so Angular
 * can hide it (or turn it into an error state) once the app is ready.
 *
 * Kept as plain ES5 and dependency-free: it must work on the slowest device/browser.
 */
(function () {
  'use strict';

  var doc = document;
  var root = doc.documentElement;

  function readStored(key) {
    try {
      return localStorage.getItem(key);
    } catch (e) {
      /* Storage can be unavailable in private mode: fall back to the system settings. */
      return null;
    }
  }

  /*
   * Theme: same storage key as ThemeService. A stored light/dark choice wins, otherwise the
   * system preference applies. It is exposed as data-splash-theme (not the theme-* classes)
   * because ThemeService rewrites those classes while the splash is still fading out.
   */
  var storedTheme = readStored('territory.theme');
  var prefersDark = false;
  try {
    prefersDark = !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
  } catch (e) {
    /* Keep the light theme. */
  }
  var dark = storedTheme === 'dark' || (storedTheme !== 'light' && prefersDark);
  root.classList.toggle('theme-dark', dark);
  root.classList.toggle('theme-light', !dark);
  root.setAttribute('data-splash-theme', dark ? 'dark' : 'light');

  /* Android status/navigation bars: follow the effective theme (a stored choice can differ from the system one). */
  try {
    var metas = doc.querySelectorAll('meta[name="theme-color"]');
    for (var m = metas.length - 1; m > 0; m--) {
      metas[m].parentNode.removeChild(metas[m]);
    }
    if (metas.length) {
      metas[0].removeAttribute('media');
      metas[0].setAttribute('content', dark ? '#0d1020' : '#0a2a6b');
    }
  } catch (e) {
    /* Keep the static meta tags. */
  }

  /* Language: same storage key as LanguageService. A stored choice wins, otherwise the system languages. */
  var lang = 'en';
  var storedLang = readStored('territory.language');
  if (storedLang === 'it' || storedLang === 'en') {
    lang = storedLang;
  } else {
    try {
      var langs = navigator.languages || [navigator.language];
      for (var i = 0; i < langs.length; i++) {
        var base = (langs[i] || '').toLowerCase().split('-')[0];
        if (base === 'it' || base === 'en') {
          lang = base;
          break;
        }
      }
    } catch (e) {
      /* Keep English. */
    }
  }
  root.lang = lang;

  var TEXT = {
    it: {
      name: 'Territori',
      subtitle: 'Registro territori',
      phases: [
        'Avvio dell’applicazione…',
        'Caricamento delle risorse…',
        'Connessione al server…',
        'Il server si sta avviando, un momento…'
      ],
      offline: 'Sei offline: avvio dalla cache…',
      error: 'Impossibile avviare l’applicazione.',
      retry: 'Riprova'
    },
    en: {
      name: 'Territory',
      subtitle: 'Territory Register',
      phases: [
        'Starting the app…',
        'Loading resources…',
        'Connecting to the server…',
        'The server is starting, one moment…'
      ],
      offline: 'You are offline: starting from cache…',
      error: 'The app could not start.',
      retry: 'Try again'
    }
  };
  var text = TEXT[lang];

  var splash = doc.getElementById('app-splash');
  if (!splash) {
    return;
  }
  var title = doc.getElementById('app-splash-title');
  var subtitle = doc.getElementById('app-splash-subtitle');
  var phase = doc.getElementById('app-splash-phase');
  var retry = doc.getElementById('app-splash-retry');
  doc.title = text.name;
  if (title) {
    title.textContent = text.name;
  }
  if (subtitle) {
    subtitle.textContent = text.subtitle;
  }
  if (retry) {
    retry.textContent = text.retry;
    retry.addEventListener('click', function () {
      window.location.reload();
    });
  }

  var hidden = false;
  var failed = false;
  var phaseIndex = 0;
  var timers = [];

  function isOnline() {
    return navigator.onLine !== false;
  }

  function setPhase(index) {
    phaseIndex = index;
    if (!phase || failed) {
      return;
    }
    phase.textContent = isOnline() ? text.phases[index] : text.offline;
  }

  function schedule(delay, index) {
    timers.push(
      window.setTimeout(function () {
        setPhase(index);
      }, delay)
    );
  }

  /* The markup carries Italian placeholders: translate the first status right away. */
  setPhase(0);

  /* Progressive, honest status messages while the app boots (cold-start hosting can take a while). */
  schedule(2500, 1);
  schedule(7000, 2);
  schedule(15000, 3);

  /* If startup is still stuck after 12 seconds, let the user reload without waiting blindly. */
  timers.push(
    window.setTimeout(function () {
      if (!hidden && !failed && retry) {
        retry.hidden = false;
      }
    }, 12000)
  );

  function refreshConnectivity() {
    if (!failed) {
      setPhase(phaseIndex);
    }
  }
  window.addEventListener('online', refreshConnectivity);
  window.addEventListener('offline', refreshConnectivity);

  /*
   * Wake the server immediately. When this page was served by the service worker the
   * backend may be cold (shared hosting), and Angular's first API call would otherwise
   * start only after the bundle has been parsed and executed.
   */
  if (isOnline()) {
    try {
      fetch('/health/live', { cache: 'no-store', credentials: 'same-origin', keepalive: true }).catch(function () {
        /* The app handles connectivity errors itself. */
      });
    } catch (e) {
      /* Older browser without fetch: ignore. */
    }
  }

  function stopTimers() {
    for (var i = 0; i < timers.length; i++) {
      window.clearTimeout(timers[i]);
    }
    timers = [];
  }

  function hide() {
    if (hidden || failed) {
      return;
    }
    hidden = true;
    stopTimers();
    splash.classList.add('app-splash--hidden');
    splash.setAttribute('aria-busy', 'false');
    window.setTimeout(function () {
      if (splash.parentNode) {
        splash.parentNode.removeChild(splash);
      }
    }, 500);
  }

  function fail() {
    if (hidden || failed) {
      return;
    }
    failed = true;
    stopTimers();
    splash.classList.add('app-splash--error');
    splash.setAttribute('aria-busy', 'false');
    if (phase) {
      phase.textContent = text.error;
    }
    if (retry) {
      retry.hidden = false;
    }
  }

  window.__territorySplash = { hide: hide, fail: fail };
})();
