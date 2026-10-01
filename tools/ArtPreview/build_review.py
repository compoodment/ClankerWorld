#!/usr/bin/env python3
"""Builds the ClankerWorld art review page from baseline and proposed renders."""
import argparse, base64, json, os, re, html, glob
import markdown

FAMILY_TITLES = {
    'terrain': 'Ground tiles, hills and mountains',
    'water': 'Water, shores and fords',
    'roads': 'Roads and bridges',
    'nature': 'Trees and natural sites',
    'crops': 'Fields and orchards',
    'buildings': 'Buildings',
    'agents': 'Agents and mounts',
    'items': 'Item icons',
    'glyphs': 'Interface icons',
    'menu': 'Main Menu valley',
    'brand': 'Logo and program icon',
}
FAMILY_INTRO = {
    'terrain': 'Each surface has a clean and a busier variant; the game draws the busier one on about one tile in four. The 16 px versions are what you see at mid zoom.',
    'water': 'Water is one seamless 16-by-16-tile block per style, so crests continue across tile edges. Shores are tinted masks drawn over the water. The reference scene shows how they meet.',
    'roads': 'Road pieces join only along the Road; corners, diagonals, ends and doorstep paths are generated for every neighbour combination. Shown here are representative pieces over grass.',
    'nature': 'One tree or site per tile, kept inside the tile with a soft shadow to the south-east. States (picked, harvested, depleted) are functional, not cosmetic.',
    'crops': 'Field states for the three farmed crops over fertile soil, and the orchard tree stages. These are for the agreed farming content that the game does not grow yet.',
    'buildings': 'One exterior per supported footprint, door on the side facing the Road. Kinds that are agreed but not in the game yet are marked new.',
    'agents': 'Eight facings (S, SW, W, NW, N, NE, E, SE), walk frames, carrying, working, talking and hurt states, four life stages, and a first look at horses for later.',
    'items': '16-pixel icons with a complete dark outline, shown on parchment as in the panels. Items the game already uses but shows as a crate today are marked new.',
    'glyphs': 'Interface icons are not redrawn in this round. They are shown so the catalogue is complete.',
    'menu': 'The valley behind the Main Menu by day (Light theme) and at dusk (Dark theme). Clouds, smoke, birds, sparkles and lights animate in the game; this shows the still layers.',
    'brand': 'Agreed on September 29 and not redrawn. Shown for completeness only.',
}
BASELINE_ALIAS = {'terrain16': 'terrain', 'hills': 'terrain', 'nature16': 'nature',
                  'items_tools': 'items', 'items_goods': 'items'}
SKIP_BASELINE = {'edges', 'coasts', 'retired'}
ORDER = ['terrain', 'water', 'roads', 'nature', 'crops', 'buildings', 'agents', 'items', 'glyphs', 'menu', 'brand']

def data_uri(path):
    with open(path, 'rb') as f:
        return 'data:image/png;base64,' + base64.b64encode(f.read()).decode('ascii')

def slug(text):
    return re.sub(r'[^A-Za-z0-9_\-.~:@+]', '-', text)

def load_index(root):
    p = os.path.join(root, 'index.json')
    if not os.path.exists(p):
        return []
    with open(p) as f:
        return json.load(f)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--baseline', required=True)
    ap.add_argument('--proposed')
    ap.add_argument('--scene', required=True)
    ap.add_argument('--style')
    ap.add_argument('--notes', help='directory with <family>.md owner notes')
    ap.add_argument('--out', required=True)
    ap.add_argument('--questions')
    args = ap.parse_args()

    baseline = load_index(args.baseline)
    proposed = load_index(args.proposed) if args.proposed else []

    # Baseline lookup by id (ids are unique across the families we show).
    base_by_id = {}
    for e in baseline:
        if e['family'] in SKIP_BASELINE:
            continue
        base_by_id[e['id']] = e

    families = {}
    def fam(key):
        if key not in families:
            families[key] = {'key': key, 'title': FAMILY_TITLES.get(key, key), 'intro': FAMILY_INTRO.get(key, ''), 'cards': {}, 'note': ''}
        return families[key]

    def card(fkey, cid):
        f = fam(fkey)
        if cid not in f['cards']:
            f['cards'][cid] = {'id': cid, 'current': None, 'proposed': None, 'note': '', 'kind': 'kept', 'cw': 0, 'ch': 0, 'pw': 0, 'ph': 0}
        return f['cards'][cid]

    for e in baseline:
        if e['family'] in SKIP_BASELINE:
            continue
        fkey = BASELINE_ALIAS.get(e['family'], e['family'])
        c = card(fkey, e['id'])
        c['current'] = data_uri(os.path.join(args.baseline, e['path']))
        c['cw'], c['ch'] = e['width'], e['height']

    matched = set()
    for e in proposed:
        pid = e['id']
        if '.sprite' in pid:
            continue
        fkey = BASELINE_ALIAS.get(e['family'], e['family'])
        # Pair with the baseline by id; agents pair their south-facing still with the old single sprite.
        bid = pid
        if fkey == 'agents' and pid not in base_by_id:
            m = re.match(r'^(infant|child|adult|elder)\.v(\d)\.S$', pid)
            if m:
                bid = f'{m.group(1)}.v{m.group(2)}'
        if fkey == 'crops' and bid in base_by_id:
            # Orchard stages were in the nature baseline; show them under crops with their current look.
            pass
        c = card(fkey, bid if bid in base_by_id else pid)
        if bid in base_by_id and c['current'] is None:
            b = base_by_id[bid]
            c['current'] = data_uri(os.path.join(args.baseline, b['path']))
            c['cw'], c['ch'] = b['width'], b['height']
        c['proposed'] = data_uri(os.path.join(args.proposed, e['path']))
        c['pw'], c['ph'] = e['width'], e['height']
        c['note'] = e.get('note') or ''
        c['kind'] = 'replaced' if c['current'] else 'new'
        c['pid'] = pid
        matched.add(bid)

    # Owner notes per family.
    if args.notes:
        for path in glob.glob(os.path.join(args.notes, '*.md')):
            key = os.path.splitext(os.path.basename(path))[0].lower()
            key = {'itemstools': 'items', 'itemsgoods': 'items', 'items_tools': 'items', 'items_goods': 'items'}.get(key, key)
            with open(path) as f:
                text = f.read()
            if key in families:
                families[key]['note'] += markdown.markdown(text, extensions=['tables', 'fenced_code'])

    style_html = ''
    if args.style and os.path.exists(args.style):
        with open(args.style) as f:
            style_html = markdown.markdown(f.read(), extensions=['tables', 'fenced_code', 'toc'])

    def scene(name):
        p = os.path.join(args.scene, name)
        return data_uri(p) if os.path.exists(p) else None

    scenes = {
        'current32': scene('scene-current-32.png'), 'current16': scene('scene-current-16.png'),
        'proposed32': scene('scene-proposed-32.png'), 'proposed16': scene('scene-proposed-16.png'),
    }

    proposed_ids = {c['id'] for f in families.values() for c in f['cards'].values() if c['proposed']}
    for f in families.values():
        for cid in [cid for cid, c in f['cards'].items() if not c['proposed'] and cid in proposed_ids]:
            del f['cards'][cid]

    ordered = []
    for key in ORDER:
        if key in families:
            f = families[key]
            cards = list(f['cards'].values())
            # New and replaced first, kept last; keep stable id order inside.
            rank = {'replaced': 0, 'new': 1, 'kept': 2}
            cards.sort(key=lambda c: (rank[c['kind']], c['id']))
            ordered.append({**f, 'cards': cards})

    data = {'families': ordered, 'scenes': scenes, 'hasProposed': bool(proposed)}
    payload = json.dumps(data).replace('</', '<\\/')

    questions_html = ''
    if args.questions and os.path.exists(args.questions):
        with open(args.questions) as f:
            questions_html = markdown.markdown(f.read())
    page = TEMPLATE.replace('__STYLE_HTML__', style_html).replace('__QUESTIONS__', questions_html).replace('__DATA__', payload)
    with open(args.out, 'w') as f:
        f.write(page)
    total = sum(len(f['cards']) for f in ordered)
    print(f"wrote {args.out}: {len(ordered)} families, {total} cards, {os.path.getsize(args.out)/1e6:.1f} MB")

TEMPLATE = r'''<title>ClankerWorld Art Review</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Pixelify+Sans:wght@500;700&family=Atkinson+Hyperlegible:ital,wght@0,400;0,700;1,400&display=swap">
<style>
/* Layout: one reading column with a sticky review bar; family sections hold a card grid. */
:root {
  --bg: #efe4cb; --surface: #f8f0dc; --surface2: #e5d7b8; --ink: #33261a; --muted: #6f5d48;
  --wood: #6b4428; --wood-dark: #3f2716; --accent: #3e7d3a; --accent-ink: #ffffff;
  --warn: #b8612a; --bad: #a0362c; --good: #3e7d3a; --line: #cdbb97;
  --display: "Pixelify Sans", "Trebuchet MS", sans-serif;
  --body: "Atkinson Hyperlegible", "Segoe UI", system-ui, sans-serif;
  --zoom: 3;
}
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) {
  --bg: #1c120a; --surface: #2a1a0e; --surface2: #3a2616; --ink: #eadfc4; --muted: #b9a888;
  --wood: #9c6c42; --wood-dark: #6e4a2e; --accent: #5aa352; --accent-ink: #10180f;
  --warn: #e0894a; --bad: #d9674f; --good: #6cbf62; --line: #5a4330; color-scheme: dark; } }
:root[data-theme="dark"] {
  --bg: #1c120a; --surface: #2a1a0e; --surface2: #3a2616; --ink: #eadfc4; --muted: #b9a888;
  --wood: #9c6c42; --wood-dark: #6e4a2e; --accent: #5aa352; --accent-ink: #10180f;
  --warn: #e0894a; --bad: #d9674f; --good: #6cbf62; --line: #5a4330; color-scheme: dark; }
body { background: var(--bg); color: var(--ink); font-family: var(--body); font-size: 16px; line-height: 1.5; margin: 0; }
.wrap { max-width: 1180px; margin: 0 auto; padding-block: 0 64px; padding-inline: 16px; }
h1, h2, h3 { font-family: var(--display); font-weight: 700; letter-spacing: 0.01em; text-wrap: balance; margin: 0; }
h1 { font-size: 2rem; } h2 { font-size: 1.5rem; } h3 { font-size: 1.1rem; }
p { max-width: 68ch; }
a { color: var(--accent); }
.bar { position: sticky; top: env(safe-area-inset-top, 0px); z-index: 5; background: var(--surface); border-bottom: 3px solid var(--wood); box-shadow: 0 2px 0 var(--wood-dark); }
.bar .in { max-width: 1180px; margin: 0 auto; padding: 8px 16px; display: flex; flex-wrap: wrap; gap: 8px 16px; align-items: center; }
.bar .title { font-family: var(--display); font-size: 1.15rem; font-weight: 700; }
.progress { display: flex; align-items: center; gap: 8px; font-variant-numeric: tabular-nums; }
.meter { width: 140px; height: 10px; background: var(--surface2); border: 2px solid var(--wood-dark); position: relative; }
.meter > i { position: absolute; inset: 0; width: 0; background: var(--accent); }
.ctl { display: flex; gap: 4px; align-items: center; flex-wrap: wrap; }
.ctl label { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--muted); }
button, select, textarea, input { font: inherit; }
.btn { border: 2px solid var(--wood-dark); background: var(--surface2); color: var(--ink); padding: 4px 10px; cursor: pointer; box-shadow: inset 0 -3px 0 rgba(0,0,0,0.12); }
.btn:hover { filter: brightness(1.05); }
.btn:focus-visible, .seg button:focus-visible, textarea:focus-visible, select:focus-visible { outline: 3px solid var(--accent); outline-offset: 2px; }
.btn.on { background: var(--accent); color: var(--accent-ink); }
.btn[disabled] { opacity: 0.5; cursor: not-allowed; }
.seg { display: inline-flex; border: 2px solid var(--wood-dark); }
.seg button { border: 0; background: var(--surface2); color: var(--ink); padding: 4px 10px; cursor: pointer; border-right: 2px solid var(--wood-dark); }
.seg button:last-child { border-right: 0; }
.seg button.on { background: var(--wood); color: #fff; }
.lead { padding-block: 24px 8px; display: grid; gap: 12px; }
.lead .how { background: var(--surface); border: 2px solid var(--wood); padding: 12px 16px; display: grid; gap: 6px; }
.lead .how li { margin: 0; }
.status { font-size: 0.9rem; color: var(--muted); }
section.fam { margin-top: 36px; }
.famhead { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 8px 16px; border-bottom: 3px solid var(--wood); padding-bottom: 6px; }
.famhead .counts { font-size: 0.85rem; color: var(--muted); font-variant-numeric: tabular-nums; }
.intro { color: var(--muted); margin-top: 8px; }
.note { background: var(--surface); border-left: 4px solid var(--wood); padding: 8px 16px; margin-top: 12px; max-width: 80ch; }
.note > summary { font-family: var(--display); font-size: 1rem; cursor: pointer; padding-block: 4px; }
.note h1, .note h2, .note h3 { font-family: var(--body); font-size: 1rem; margin-top: 12px; }
.note ul { padding-left: 20px; }
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 14px; margin-top: 16px; }
.card { background: var(--surface); border: 2px solid var(--line); padding: 10px; display: grid; gap: 8px; min-width: 0; }
.card.decided-approve { border-color: var(--good); }
.card.decided-reject { border-color: var(--bad); }
.card.decided-change { border-color: var(--warn); }
.card.wide { grid-column: 1 / -1; }
.card .idrow { display: flex; justify-content: space-between; gap: 8px; align-items: baseline; }
.card .id { font-family: ui-monospace, "Cascadia Mono", Menlo, monospace; font-size: 0.85rem; word-break: break-all; }
.tag { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.08em; padding: 1px 6px; border: 1px solid currentColor; white-space: nowrap; }
.tag.new { color: var(--accent); }
.tag.kept { color: var(--muted); }
.tag.replaced { color: var(--wood); }
.pics { display: flex; gap: 10px; flex-wrap: wrap; align-items: flex-start; }
.pic { display: grid; gap: 2px; justify-items: start; min-width: 0; max-width: 100%; }
.pic small { font-size: 0.72rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--muted); }
.pic img { image-rendering: pixelated; image-rendering: crisp-edges; display: block; max-width: 100%; height: auto; background: var(--surface2); }
.pic.on-parchment img { background: #e9dcc0; }
.pic.on-grass img { background: #5f8f5b; }
.pic.on-dark img { background: #2b2b2b; }
.pic.on-checker img { background-image: linear-gradient(45deg, #8f8f8f 25%, transparent 25%), linear-gradient(-45deg, #8f8f8f 25%, transparent 25%), linear-gradient(45deg, transparent 75%, #8f8f8f 75%), linear-gradient(-45deg, transparent 75%, #8f8f8f 75%); background-size: 16px 16px; background-position: 0 0, 0 8px, 8px -8px, -8px 0; background-color: #7a7a7a; }
.card .entrynote { font-size: 0.9rem; color: var(--muted); }
.decide { display: flex; gap: 6px; flex-wrap: wrap; }
.decide .btn { padding: 3px 9px; font-size: 0.9rem; }
.decide .btn.approve.on { background: var(--good); color: #fff; }
.decide .btn.change.on { background: var(--warn); color: #fff; }
.decide .btn.reject.on { background: var(--bad); color: #fff; }
textarea.cnote { width: 100%; box-sizing: border-box; min-height: 2.4em; resize: vertical; border: 2px solid var(--line); background: var(--surface2); color: var(--ink); padding: 4px 6px; }
.scene-wrap { display: grid; gap: 16px; margin-top: 16px; }
.compare { position: relative; overflow: hidden; border: 3px solid var(--wood); background: #000; max-width: 100%; }
.compare img { display: block; image-rendering: pixelated; width: 100%; height: auto; }
.compare .over { position: absolute; inset: 0; clip-path: inset(0 50% 0 0); }
.compare .over img { width: 100%; }
.compare .handle { position: absolute; top: 0; bottom: 0; width: 3px; background: #fff; left: 50%; pointer-events: none; box-shadow: 0 0 0 1px #000; }
.compare .lbl { position: absolute; top: 6px; font-family: var(--display); font-size: 0.9rem; padding: 2px 8px; background: rgba(0,0,0,0.6); color: #fff; }
.compare .lbl.l { left: 6px; } .compare .lbl.r { right: 6px; }
input[type=range].slider { width: 100%; }
.questions { background: var(--surface); border: 2px solid var(--warn); padding: 4px 16px 10px; margin-top: 14px; max-width: 80ch; }
.questions h3 { margin-top: 10px; }
.questions li { margin-block: 4px; }
.overall { background: var(--surface); border: 2px solid var(--wood); padding: 10px 14px; margin-top: 14px; display: grid; gap: 8px; max-width: 80ch; }
.guide { background: var(--surface); border: 2px solid var(--wood); padding: 8px 18px 18px; margin-top: 16px; }
.guide h1 { font-size: 1.4rem; margin-top: 12px; } .guide h2 { font-size: 1.15rem; margin-top: 18px; } .guide h3 { font-size: 1rem; margin-top: 12px; }
.guide table { border-collapse: collapse; font-size: 0.9rem; } .guide th, .guide td { border: 1px solid var(--line); padding: 3px 8px; text-align: left; }
.guide .tablewrap { overflow-x: auto; }
.guide code { background: var(--surface2); padding: 0 4px; }
.guide pre { overflow-x: auto; background: var(--surface2); padding: 8px; }
.guide ul { padding-left: 20px; }
details > summary { cursor: pointer; font-family: var(--display); font-size: 1.1rem; padding: 6px 0; }
.toast { position: fixed; bottom: calc(16px + env(safe-area-inset-bottom, 0px)); left: 50%; transform: translateX(-50%); background: var(--wood-dark); color: #fff; padding: 8px 14px; border: 2px solid #000; z-index: 9; }
.toast[hidden] { display: none; }
@media (prefers-reduced-motion: no-preference) { .card { transition: border-color 0.15s; } }
@media (max-width: 520px) { .meter { width: 90px; } h1 { font-size: 1.6rem; } }
</style>
<div class="bar"><div class="in">
  <span class="title">Art review</span>
  <span class="progress"><span class="meter"><i id="meter"></i></span><span id="progress">0 / 0 decided</span></span>
  <span class="ctl"><label>Zoom</label><span class="seg" id="zoom"><button data-z="1">1×</button><button data-z="2">2×</button><button data-z="3" class="on">3×</button><button data-z="4">4×</button><button data-z="6">6×</button></span></span>
  <span class="ctl"><label>Show</label><span class="seg" id="filter"><button data-f="changed" class="on">New and redrawn</button><button data-f="undecided">Undecided</button><button data-f="all">Everything</button></span></span>
  <button class="btn" id="copy">Copy decisions</button>
  <span class="status" id="dbstatus">Connecting…</span>
</div></div>
<div class="wrap">
  <div class="lead">
    <h1>ClankerWorld art: current versus proposed</h1>
    <p>Every texture the game draws today is shown beside its proposed improvement, drawn in code the same way. Round 1 covers a reference set from every family; the rest follows once you like the direction. Nothing here is in the game yet. Your decisions save as you click, and I read them back from this page.</p>
    <div class="how">
      <strong>How to review</strong>
      <ul>
        <li><b>Approve</b> means merge it as shown. <b>Change</b> means keep the direction but fix what you write in the note. <b>Reject</b> means keep the current art (or, for a new asset, do not add it).</li>
        <li>Judge at 1× first (that is what players see at full zoom), then zoom in. The 16 px entries are the mid-zoom atlas.</li>
        <li>Start with the style direction and the reference scene below; a decision there covers the look as a whole.</li>
      </ul>
    </div>
  </div>

  <section class="fam" id="direction">
    <div class="famhead"><h2>Style direction</h2><span class="counts" data-counts="direction"></span></div>
    <p class="intro">The reference scene is the same Town corner drawn with current art and with the proposed art, at full zoom (32 px tiles) and at mid zoom (16 px). Drag the slider to compare.</p>
    <div class="scene-wrap" id="scenes"></div>
    <div class="questions">__QUESTIONS__</div>
    <div class="overall" data-card="direction~overall">
      <h3>Overall direction</h3>
      <div class="decide"></div>
      <textarea class="cnote" placeholder="What should change about the direction as a whole? (optional)"></textarea>
    </div>
    <details class="guide" open><summary>Style guide</summary>
      <div id="styleguide">__STYLE_HTML__</div>
    </details>
  </section>

  <div id="families"></div>
</div>
<div class="toast" id="toast" hidden></div>
<script>
const DATA = __DATA__;
const state = { zoom: 3, filter: 'changed', decisions: {}, db: null, canWrite: null };
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

function el(tag, attrs = {}, children = []) {
  const n = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === 'class') n.className = v; else if (k === 'text') n.textContent = v; else if (k.startsWith('data-')) n.setAttribute(k, v); else n[k] = v;
  }
  for (const c of children) if (c) n.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
  return n;
}

function backdropFor(family) {
  if (family === 'items' || family === 'glyphs') return 'on-parchment';
  if (family === 'water' || family === 'menu' || family === 'terrain') return 'on-dark';
  if (family === 'brand') return 'on-grass';
  return 'on-grass';
}

function pic(label, src, w, h, backdrop) {
  const img = el('img', { src, alt: label, width: w * state.zoom, height: h * state.zoom, loading: 'lazy' });
  img.dataset.w = w; img.dataset.h = h;
  return el('div', { class: 'pic ' + backdrop }, [el('small', { text: label }), img]);
}

function decideRow(key) {
  const row = el('div', { class: 'decide' });
  for (const d of ['approve', 'change', 'reject']) {
    const b = el('button', { class: 'btn ' + d, text: d === 'approve' ? 'Approve' : d === 'change' ? 'Change' : 'Reject' });
    b.addEventListener('click', () => setDecision(key, d));
    row.appendChild(b);
  }
  return row;
}

function docId(key) { return key.replace(/[^A-Za-z0-9_\-.~:@+]/g, '-'); }

function render() {
  const root = $('#families');
  root.textContent = '';
  for (const f of DATA.families) {
    const sec = el('section', { class: 'fam', id: 'fam-' + f.key });
    sec.appendChild(el('div', { class: 'famhead' }, [el('h2', { text: f.title }), el('span', { class: 'counts', 'data-counts': f.key })]));
    if (f.intro) sec.appendChild(el('p', { class: 'intro', text: f.intro }));
    if (f.note) { const d = el('details', { class: 'note' }, [el('summary', { text: 'What changed and why' })]); const n = el('div'); n.innerHTML = f.note; d.appendChild(n); sec.appendChild(d); }
    if (f.cards.some(c => c.kind !== 'kept')) {
      const overall = el('div', { class: 'overall', 'data-card': f.key + '~overall' }, [el('h3', { text: 'This family as a whole' }), decideRow(f.key + '~overall')]);
      const ta = el('textarea', { class: 'cnote', placeholder: 'Notes for the whole family (optional)' });
      overall.appendChild(ta); bindNote(ta, f.key + '~overall');
      sec.appendChild(overall);
    }
    const grid = el('div', { class: 'grid' });
    for (const c of f.cards) {
      const key = f.key + '~' + c.id;
      const card = el('div', { class: 'card' + (Math.max(c.cw, c.pw) > 96 ? ' wide' : ''), 'data-card': key, 'data-kind': c.kind });
      card.appendChild(el('div', { class: 'idrow' }, [el('span', { class: 'id', text: c.id }), el('span', { class: 'tag ' + c.kind, text: c.kind === 'new' ? 'new' : c.kind === 'replaced' ? 'redrawn' : (f.key === 'brand' ? 'agreed, unchanged' : 'later round') })]));
      const pics = el('div', { class: 'pics' });
      const bd = backdropFor(f.key);
      if (c.current) pics.appendChild(pic('current', c.current, c.cw, c.ch, bd));
      if (c.proposed) pics.appendChild(pic('proposed', c.proposed, c.pw, c.ph, bd));
      card.appendChild(pics);
      if (c.note) card.appendChild(el('div', { class: 'entrynote', text: c.note }));
      if (c.kind !== 'kept') {
        card.appendChild(decideRow(key));
        const note = el('textarea', { class: 'cnote', placeholder: 'Note (optional; say what to change)' });
        bindNote(note, key);
        card.appendChild(note);
      }
      grid.appendChild(card);
    }
    sec.appendChild(grid);
    root.appendChild(sec);
  }
  renderScenes();
  bindNote($('#direction .overall textarea'), 'direction~overall');
  $('#direction .overall .decide').replaceWith(decideRow('direction~overall'));
  // Make tables in the guide scroll instead of widening the page.
  $$('#styleguide table').forEach(t => { const w = el('div', { class: 'tablewrap' }); t.replaceWith(w); w.appendChild(t); });
  applyDecisions();
  applyFilter();
}

function compare(label, cur, prop, scale) {
  const box = el('div', { class: 'compare' });
  const under = el('img', { src: cur, alt: 'current ' + label });
  const over = el('div', { class: 'over' }, [el('img', { src: prop, alt: 'proposed ' + label })]);
  const handle = el('div', { class: 'handle' });
  box.append(under, over, handle, el('span', { class: 'lbl l', text: 'proposed' }), el('span', { class: 'lbl r', text: 'current' }));
  const range = el('input', { type: 'range', min: 0, max: 100, value: 50, class: 'slider', id: 'slider-' + label.replace(/\W/g, '') });
  const update = () => { over.style.clipPath = `inset(0 ${100 - range.value}% 0 0)`; handle.style.left = range.value + '%'; };
  range.addEventListener('input', update); update();
  return el('div', {}, [el('h3', { text: label }), box, range]);
}

function renderScenes() {
  const w = $('#scenes'); w.textContent = '';
  const s = DATA.scenes;
  if (s.current32 && s.proposed32) w.appendChild(compare('Full zoom (32 px tiles)', s.current32, s.proposed32));
  else if (s.current32) w.appendChild(el('div', {}, [el('h3', { text: 'Full zoom (32 px tiles), current art' }), el('div', { class: 'compare' }, [el('img', { src: s.current32, alt: 'current scene' })])]));
  if (s.current16 && s.proposed16) w.appendChild(compare('Mid zoom (16 px tiles)', s.current16, s.proposed16));
  else if (s.current16) w.appendChild(el('div', {}, [el('h3', { text: 'Mid zoom (16 px tiles), current art' }), el('div', { class: 'compare' }, [el('img', { src: s.current16, alt: 'current scene at 16 px' })])]));
}

function setZoom(z) {
  state.zoom = z;
  $$('#zoom button').forEach(b => b.classList.toggle('on', +b.dataset.z === z));
  $$('.pic img').forEach(img => { img.width = +img.dataset.w * z; img.height = +img.dataset.h * z; });
  try { localStorage.setItem('art-review-zoom', String(z)); } catch (e) {}
}

function applyFilter() {
  $$('.card').forEach(card => {
    const key = card.dataset.card;
    const d = state.decisions[key];
    let show = true;
    if (state.filter === 'undecided') show = card.dataset.kind !== 'kept' && (!d || !d.decision);
    if (state.filter === 'changed') show = card.dataset.kind !== 'kept';
    card.hidden = !show;
  });
}

function applyDecisions() {
  let total = 0, decided = 0;
  const perFamily = {};
  $$('[data-card]').forEach(card => {
    if (card.dataset.kind === 'kept') return;
    const key = card.dataset.card;
    const d = state.decisions[key] || {};
    card.classList.remove('decided-approve', 'decided-reject', 'decided-change');
    if (d.decision) card.classList.add('decided-' + d.decision);
    $$('.decide .btn', card).forEach(b => b.classList.toggle('on', b.classList.contains(d.decision || '-')));
    const ta = $('textarea', card);
    if (ta && document.activeElement !== ta && (d.note || '') !== ta.value) ta.value = d.note || '';
    const fam = key.split('~')[0];
    perFamily[fam] = perFamily[fam] || { t: 0, d: 0 };
    perFamily[fam].t++; total++;
    if (d.decision) { perFamily[fam].d++; decided++; }
  });
  $('#progress').textContent = `${decided} / ${total} decided`;
  $('#meter').style.width = total ? (100 * decided / total) + '%' : '0';
  $$('[data-counts]').forEach(s => { const c = perFamily[s.dataset.counts]; s.textContent = c ? `${c.d} of ${c.t} decided` : ''; });
}

let saving = {};
async function save(key, patch) {
  const prev = state.decisions[key] || {};
  const next = { ...prev, ...patch, key, family: key.split('~')[0], id: key.split('~').slice(1).join('~'), updated: new Date().toISOString() };
  state.decisions[key] = next;
  applyDecisions(); applyFilter();
  if (!state.db) { try { localStorage.setItem('art-review-' + key, JSON.stringify(next)); } catch (e) {} return; }
  if (saving[key]) { saving[key].then(() => save(key, patch)); return; }
  saving[key] = state.db.doc('reviews/' + docId(key)).set(next).then(() => { delete saving[key]; }).catch(e => {
    delete saving[key];
    if (e && e.code === 'invalid_argument') { state.canWrite = false; readOnly('This page is read-only for you; decisions are not saved.'); }
    else toast('Could not save: ' + (e && e.message ? e.message : 'unknown error'));
  });
}
function setDecision(key, decision) {
  const cur = state.decisions[key];
  save(key, { decision: cur && cur.decision === decision ? '' : decision });
}
function bindNote(ta, key) {
  let timer;
  ta.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => save(key, { note: ta.value }), 700); });
  ta.addEventListener('blur', () => { clearTimeout(timer); const d = state.decisions[key] || {}; if ((d.note || '') !== ta.value) save(key, { note: ta.value }); });
}
function readOnly(msg) {
  $$('.decide .btn').forEach(b => b.disabled = true);
  $$('textarea.cnote').forEach(t => t.disabled = true);
  $('#dbstatus').textContent = msg;
}
let toastTimer;
function toast(msg) { const t = $('#toast'); t.textContent = msg; t.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => { t.hidden = true; }, 3000); }

function decisionsText() {
  const lines = [];
  const byFam = {};
  for (const d of Object.values(state.decisions)) { if (!d.decision && !d.note) continue; (byFam[d.family] = byFam[d.family] || []).push(d); }
  for (const [fam, list] of Object.entries(byFam)) {
    lines.push(`## ${fam}`);
    for (const d of list.sort((a, b) => a.id.localeCompare(b.id))) lines.push(`- ${d.id}: ${d.decision || 'no decision'}${d.note ? ' — ' + d.note.replace(/\s+/g, ' ') : ''}`);
  }
  return lines.join('\n') || 'No decisions yet.';
}

$('#copy').addEventListener('click', () => {
  const text = decisionsText();
  navigator.clipboard.writeText(text).then(() => toast('Decisions copied')).catch(() => {
    const ta = el('textarea', { value: text, style: 'position:fixed;left:0;top:0;width:90vw;height:40vh;z-index:99' });
    document.body.appendChild(ta); ta.select(); toast('Select and copy the text, then click away'); ta.addEventListener('blur', () => ta.remove());
  });
});
$('#zoom').addEventListener('click', e => { const b = e.target.closest('button'); if (b) setZoom(+b.dataset.z); });
$('#filter').addEventListener('click', e => { const b = e.target.closest('button'); if (!b) return; state.filter = b.dataset.f; $$('#filter button').forEach(x => x.classList.toggle('on', x === b)); applyFilter(); });

render();
try { const z = +localStorage.getItem('art-review-zoom'); if (z) setZoom(z); } catch (e) {}

(async () => {
  const hasClaude = typeof window.claude !== 'undefined' && window.claude && typeof window.claude.use === 'function';
  if (!hasClaude) { $('#dbstatus').textContent = 'Local preview: decisions stay in this browser.'; loadLocal(); return; }
  const db = await window.claude.use('db');
  if (!db) { $('#dbstatus').textContent = 'Decisions stay in this browser only.'; loadLocal(); return; }
  state.db = db;
  const user = await window.claude.use('user');
  const can = user ? await user.can('data.write') : null;
  if (can === false) readOnly('Read-only view: your decisions are not saved.');
  else $('#dbstatus').textContent = 'Decisions save automatically.';
  db.collection('reviews').onSnapshot(snap => {
    for (const doc of snap.docs) { const d = doc.data(); if (d && d.key) state.decisions[d.key] = d; }
    applyDecisions(); applyFilter();
  }, err => { $('#dbstatus').textContent = 'Could not load saved decisions (' + err.code + ').'; });
})();

function loadLocal() {
  try { for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); if (k && k.startsWith('art-review-') && k !== 'art-review-zoom') { const d = JSON.parse(localStorage.getItem(k)); if (d && d.key) state.decisions[d.key] = d; } } } catch (e) {}
  applyDecisions(); applyFilter();
}
</script>
'''

if __name__ == '__main__':
    main()
