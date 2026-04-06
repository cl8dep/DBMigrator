/* ── GSAP + ScrollTrigger ── */
gsap.registerPlugin(ScrollTrigger);

/* ══════════════════════════════════════════
   1. HERO FADE-UP (staggered, no scroll trigger)
══════════════════════════════════════════ */
gsap.timeline({ defaults: { ease: 'power3.out' } })
  .from('#h-badge',    { y: 24, autoAlpha: 0, duration: 0.7 }, 0.15)
  .from('#h-title',    { y: 32, autoAlpha: 0, duration: 0.8 }, 0.3)
  .from('#h-sub',      { y: 24, autoAlpha: 0, duration: 0.7 }, 0.5)
  .from('#h-ctas',     { y: 20, autoAlpha: 0, duration: 0.6 }, 0.65)
  .from('#h-terminal', { y: 36, autoAlpha: 0, duration: 0.9 }, 0.75);

/* ══════════════════════════════════════════
   2. SCROLL-TRIGGERED FADE-UP for .fade-up
      (skips hero elements already animated)
══════════════════════════════════════════ */
document.querySelectorAll('.fade-up:not([id^="h-"])').forEach((el) => {
  gsap.from(el, {
    scrollTrigger: {
      trigger: el,
      start: 'top 88%',
      toggleActions: 'play none none none',
    },
    y: 30,
    autoAlpha: 0,
    duration: 0.7,
    ease: 'power2.out',
    delay: parseFloat(el.style.animationDelay || 0),
  });
});

/* ══════════════════════════════════════════
   3. TERMINAL TYPEWRITER
══════════════════════════════════════════ */
// instant:true lines appear all at once (long log lines — more realistic)
const LINES = [
  { cls: 't-prompt',  text: '$ ' },
  { cls: 't-cmd',     text: 'db-migrator run --config config.yaml\n' },
  { cls: 't-info',    text: 'Loading config from: config.yaml\n' },
  { cls: 't-info',    text: 'Running schema validation against source DB...\n' },
  { cls: 't-info',    text: '[19:13:59 INF] Connected to source DB for schema validation\n', instant: true },
  { cls: 't-ok',      text: '✓ Schema validation passed\n' },
  { cls: 't-section', text: '▶ Starting migration...\n' },
  { cls: 't-info',    text: 'Running pre-flight checks on source DB...\n' },
  { cls: 't-ok',      text: 'Pre-flight checks done.\n' },
  { cls: 't-info',    text: '[19:14:00 INF] Starting dump from nexus-prod@10.24.0.5:5432\n', instant: true },
  { cls: 't-info',    text: '[19:14:00 DBG] Executing: pg_dump --host 10.24.0.5 --port 5432 --username nexus --format directory --no-password --file /tmp/db-migrator-dump-2f8e4b1a9c3d --verbose --jobs 6 --lock-wait-timeout=60s nexus-prod\n', instant: true },
  { cls: 't-ok',      text: '[19:21:07 INF] Dump completed in 0:07:07.9172459 (1,714,801,824 bytes). Restoring to nexus-staging@10.24.0.5:5432\n', instant: true },
  { cls: 't-info',    text: '[19:21:08 DBG] Executing: pg_restore --host 10.24.0.5 --port 5432 --username nexus --dbname nexus-staging --no-password --clean --if-exists --verbose --jobs 6 /tmp/db-migrator-dump-2f8e4b1a9c3d\n', instant: true },
  { cls: 't-info',    text: '[19:36:24 INF] Restore completed in 0:15:16.5022196\n', instant: true },
  { cls: 't-info',    text: '[19:36:24 DBG] Temp dump directory deleted: /tmp/db-migrator-dump-2f8e4b1a9c3d\n', instant: true },
  { cls: 't-ok',      text: '✓ Migration completed\n' },
  { cls: 't-section', text: '▶ Starting sanitization...\n' },
  { cls: 't-info',    text: '[19:36:25 INF] Connected to target DB for sanitization\n', instant: true },
  { cls: 't-info',    text: '[19:36:25 DBG] Executing: UPDATE "users" SET "password" = @p_password, "email" = @p_email, "display_name" = @p_display_name, "first_name" = @p_first_name, "last_name" = @p_last_name, "phone_number" = @p_phone_number\n', instant: true },
  { cls: 't-info',    text: '[19:36:25 INF] Sanitized table \'users\': 12,847 row(s) updated\n', instant: true },
  { cls: 't-info',    text: '[19:36:25 INF] Sanitization transaction committed\n', instant: true },
  { cls: 't-ok',      text: '✓ Sanitization completed\n' },
  { cls: 't-info',    text: '\n' },
  { cls: 't-row',     text: '╭─────────────┬──────────────╮\n', instant: true },
  { cls: 't-row',     text: '│ Table       │ Rows Updated │\n', instant: true },
  { cls: 't-row',     text: '├─────────────┼──────────────┤\n', instant: true },
  { cls: 't-row',     text: '│ users       │       12,847 │\n', instant: true },
  { cls: 't-row',     text: '│ TOTAL       │       12,847 │\n', instant: true },
  { cls: 't-row',     text: '╰─────────────┴──────────────╯\n', instant: true },
];

const termOut = document.getElementById('term-out');
let lineIndex = 0;
let charIndex = 0;
let currentSpan = null;

function typeNextChar() {
  if (lineIndex >= LINES.length) return;

  const line = LINES[lineIndex];

  if (charIndex === 0) {
    currentSpan = document.createElement('span');
    currentSpan.className = line.cls;
    termOut.appendChild(currentSpan);
  }

  // instant lines: reveal full text at once, then move on
  if (line.instant) {
    currentSpan.textContent = line.text;
    lineIndex++;
    charIndex = 0;
    setTimeout(typeNextChar, 60);
    return;
  }

  currentSpan.textContent += line.text[charIndex];
  charIndex++;

  if (charIndex < line.text.length) {
    const delay = line.cls === 't-cmd' ? 40 : 14;
    setTimeout(typeNextChar, delay);
  } else {
    lineIndex++;
    charIndex = 0;
    const pause = line.cls === 't-cmd' ? 340 : line.cls === 't-section' ? 200 : 50;
    setTimeout(typeNextChar, pause);
  }
}

// Start typewriter after hero animation settles
setTimeout(typeNextChar, 1200);

/* ══════════════════════════════════════════
   4. TAB SWITCHER (Install section)
══════════════════════════════════════════ */
const tabBtns = document.querySelectorAll('.tab-btn');
const tabPanels = {
  local:    document.getElementById('tab-local'),
  docker:   document.getElementById('tab-docker'),
  cloudrun: document.getElementById('tab-cloudrun'),
};

function activateTab(name) {
  tabBtns.forEach(b => b.classList.toggle('active', b.dataset.tab === name));
  Object.entries(tabPanels).forEach(([key, el]) => {
    el.classList.toggle('hidden', key !== name);
  });
}

tabBtns.forEach(btn => {
  btn.addEventListener('click', () => activateTab(btn.dataset.tab));
});

/* ══════════════════════════════════════════
   5. COPY BUTTONS (config window + install)
══════════════════════════════════════════ */
function flashCopied(btn) {
  const orig = btn.textContent;
  btn.textContent = 'Copied!';
  btn.style.color = '#30d158';
  setTimeout(() => {
    btn.textContent = orig;
    btn.style.color = '';
  }, 2000);
}

// Generic copy-btn elements (data-copy attribute)
document.querySelectorAll('.copy-btn').forEach(btn => {
  btn.addEventListener('click', () => {
    const text = btn.dataset.copy.replace(/&#10;/g, '\n').replace(/&quot;/g, '"');
    navigator.clipboard.writeText(text).then(() => flashCopied(btn));
  });
});

// Install copy button — copies whichever tab is active
const installCopyBtn = document.getElementById('install-copy');
if (installCopyBtn) {
  installCopyBtn.addEventListener('click', () => {
    const activeTab = document.querySelector('.tab-btn.active');
    if (!activeTab) return;
    const panel = tabPanels[activeTab.dataset.tab];
    if (!panel) return;
    const code = panel.querySelector('code');
    if (code) navigator.clipboard.writeText(code.textContent).then(() => flashCopied(installCopyBtn));
  });
}

/* ══════════════════════════════════════════
   6. NAVBAR — subtle border on scroll
══════════════════════════════════════════ */
const navbar = document.getElementById('navbar');
window.addEventListener('scroll', () => {
  navbar.style.borderBottomColor = window.scrollY > 10
    ? 'rgba(255,255,255,0.07)'
    : 'rgba(255,255,255,0.03)';
}, { passive: true });

/* ══════════════════════════════════════════
   7. RE-HIGHLIGHT after tab switch
      (Prism doesn't auto-run on hidden els)
══════════════════════════════════════════ */
tabBtns.forEach(btn => {
  btn.addEventListener('click', () => {
    const panel = tabPanels[btn.dataset.tab];
    if (panel && window.Prism) {
      panel.querySelectorAll('code[class*="language-"]').forEach(el => {
        Prism.highlightElement(el);
      });
    }
  });
});
