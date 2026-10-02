"""Builds the round 3 save timeline page with every screenshot embedded.

Writes to ../tl2page/index.html so the published page keeps its address.
"""
import base64
import html
import re

STYLE = re.search(r'<style>(.*?)</style>', open('../page/template.html', encoding='utf-8').read(), re.S).group(1)


def uri(name):
    with open(f'img/{name}', 'rb') as file:
        return 'data:image/png;base64,' + base64.b64encode(file.read()).decode()


def shot(key, label, alt):
    imgs = ''.join(f'<img class="shot-{t}" src="{uri(f"{key}-{t}.png")}" alt="{html.escape(alt)}, {"light" if t == "l" else "dark"} theme">' for t in 'ld')
    caption = f'<figcaption>{html.escape(label)}</figcaption>' if label else ''
    return (f'<figure>{caption}'
            f'<button type="button" class="shot" data-key="{key}" data-title="{html.escape(alt)}" aria-label="Open {html.escape(alt)} full size">{imgs}</button></figure>')


FLAGS = [
    ('A', 'banner', 'Banner', [
        'A small hanging banner in the branch’s colour: cloth on a crossbar with a forked hem, on a wooden pole with a brass knob.',
        'Drawn pixel by pixel like the map’s art, where the old flag was a thin line with a smooth triangle.',
    ], 'My recommendation: still clearly a flag, but it looks like it belongs in the game.'),
    ('B', 'badge', 'Number badge', [
        'The branch’s numbered badge from the left column, on a short post.',
        'The end of every branch says which branch it is, so you don’t have to follow the line back to the left.',
    ], 'The most informative. It adds a third coloured box to each lane, so it is busier.'),
    ('C', 'arrow', 'Arrow', [
        'No flag. The branch line runs a little past its newest save and ends in an arrowhead.',
        'It reads as “the branch carries on from here”, which is what playing on from that save does.',
    ], 'The quietest. Easy to miss when you are looking for the newest save.'),
    ('–', 'pennant', 'Round 2 flag, for comparison', [
        'The thin pole and smooth triangle from round 2.',
    ], 'The one you didn’t like.'),
]

HEADERS = [
    ('A', 'none', 'Nothing', [
        'The row goes. The Timeline / List switch moves up beside the title, so the timeline sits a little higher.',
    ], 'The tidiest.'),
    ('B', 'name', 'World name', [
        'Just the world’s name beside the globe icon, in ordinary writing.',
        'The branch and save counts go: the left column already shows them.',
    ], 'Useful only if you lose track of which world you are in.'),
    ('C', 'key', 'Key to the points', [
        'A small key: newest on its branch, save, autosave. It always shows the flag you pick.',
    ], 'My recommendation: it explains the marks to someone new and fills the row with something useful.'),
    ('–', 'original', 'Round 2 line, for comparison', [
        'WILLOWMERE · 3 BRANCHES · 7 SAVES.',
    ], 'The one you called “very AI”.'),
]


def flag_html(letter, key, title, points, verdict):
    items = ''.join(f'<li>{p}</li>' for p in points)
    figures = (shot(f'flag-{key}', 'The example world', f'{title}, example world')
               + shot(f'flag-{key}-busy', 'A busy world', f'{title}, busy world'))
    return f'''
<section class="item" id="flag-{key}">
  <header class="item-head"><span class="num">{letter}</span><h3>{html.escape(title)}</h3></header>
  <ul class="changes">{items}</ul>
  <div class="pair stack">{figures}</div>
  <p class="verdict">{html.escape(verdict)}</p>
</section>'''


def header_html(letter, key, title, points, verdict):
    items = ''.join(f'<li>{html.escape(p)}</li>' for p in points)
    return f'''
<section class="item" id="head-{key}">
  <header class="item-head"><span class="num">{letter}</span><h3>{html.escape(title)}</h3></header>
  <ul class="changes">{items}</ul>
  <div class="pair stack">{shot(f'head-{key}', '', f'{title}, top of the panel')}</div>
  <p class="verdict">{html.escape(verdict)}</p>
</section>'''


def radios(name, options):
    return ''.join(
        f'<label class="choice {cls}"><input type="radio" name="{name}" value="{value}"><span>{html.escape(text)}</span></label>'
        for value, cls, text in options)


FLAG_CHOICES = [('banner', 'yes', 'Banner'), ('badge', 'yes', 'Number badge'), ('arrow', 'yes', 'Arrow'), ('none', 'no', 'None of these')]
HEADER_CHOICES = [('none', 'yes', 'Nothing'), ('name', 'yes', 'World name'), ('key', 'yes', 'Key to the points'), ('other', 'no', 'Something else')]

flags = ''.join(flag_html(*f) for f in FLAGS)
headers = ''.join(header_html(*h) for h in HEADERS)

page = f'''<title>Save Timeline Round 3</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Atkinson+Hyperlegible:ital,wght@0,400;0,700;1,400&family=IBM+Plex+Mono:wght@400;600&family=Pixelify+Sans:wght@500;600&display=swap">
<style>{STYLE}
.pair.stack figure {{ justify-items: stretch; }}
.verdict {{ margin: 0; padding: 10px 12px; border-left: 3px solid var(--wood); background: var(--bg); max-width: 78ch; }}
.item h3 {{ font: 600 20px/1.2 var(--font-display); margin: 0; }}
.group-head {{ display: grid; gap: 6px; margin: 34px 0 6px; }}
.group-head h2 {{ font: 600 26px/1.15 var(--font-display); margin: 0; }}
.group-head p {{ margin: 0; max-width: 78ch; }}
.decide-box {{ background: var(--sheet); border: 1px solid var(--accent); box-shadow: inset 4px 0 0 var(--accent); border-radius: 4px; padding: 18px 20px; display: grid; gap: 14px; margin-block: 28px 18px; }}
.decide-box h2 {{ font: 600 22px/1.2 var(--font-display); margin: 0; }}
.decide-box h3 {{ font: 600 16px/1.2 var(--font-body, inherit); margin: 0; }}
.done {{ display: grid; gap: 4px; padding-left: 18px; margin: 0; }}
</style>

<div class="wrap">
  <header class="intro">
    <p class="eyebrow">ClankerWorld · issue #680 · save branches · round 3</p>
    <h1>Save Timeline Round 3</h1>
    <p class="lede">You picked Clean, with three changes: the first drawing’s season bar back, better flags, and something other than the “WILLOWMERE · 4 BRANCHES · 19 SAVES” line. The season bar is done. For the flags and that line there are options below, so pick one of each.</p>
    <ul class="facts"><li>2 quick choices</li><li>1920 × 1080, automatic UI size</li><li>light and dark theme</li></ul>
  </header>

  <section class="notes" aria-label="About these mockups">
    <div class="note-box ask">
      <h2>What I need from you</h2>
      <p>Pick a flag and pick what goes above the timeline, add a note if you like, then press <strong>Copy answer</strong> at the bottom and paste it into chat. Building still waits on #682.</p>
    </div>
    <div class="note-box">
      <h2>Already changed</h2>
      <ul class="done">
        <li>The season bar is the first drawing’s again: a thin band in each season’s colour, a tick per day (longer where a season starts), the season’s icon and name, “·&nbsp;year&nbsp;2” after a new year, and day numbers where there is room.</li>
        <li>Kept from Clean: the soft season colour behind the lanes and the lines where seasons and years start. The black “Year 2” tag is gone, since the bar names the year now.</li>
      </ul>
    </div>
    <div class="note-box">
      <h2>Good to know</h2>
      <ul>
        <li>The flag pictures all use the key above the timeline (header option C), so you can see the key with each flag.</li>
        <li>The busy pictures are cut off at the bottom by the mockup, not the design. The real panel fits the screen.</li>
      </ul>
    </div>
  </section>

  <div class="bar" role="toolbar" aria-label="Picture controls">
    <span class="bar-label" id="shots-label">Pictures</span>
    <div class="seg" role="group" aria-labelledby="shots-label">
      <button type="button" id="shots-l" aria-pressed="true">Light game</button>
      <button type="button" id="shots-d" aria-pressed="false">Dark game</button>
    </div>
    <span class="bar-label">Click any picture to see it full size.</span>
  </div>

  <main>
    <div class="group-head"><h2>1. The flag on each branch’s newest save</h2><p>It shows which save you can play on from without starting a new branch.</p></div>
    {flags}
    <div class="group-head"><h2>2. The line above the timeline</h2><p>What sits to the left of the Timeline / List switch.</p></div>
    {headers}
    <section class="decide-box" aria-labelledby="decide-title">
      <h2 id="decide-title">Your choice</h2>
      <h3 id="flag-title">Flag</h3>
      <div class="choices" role="radiogroup" aria-labelledby="flag-title">{radios('flag', FLAG_CHOICES)}</div>
      <h3 id="head-title">Above the timeline</h3>
      <div class="choices" role="radiogroup" aria-labelledby="head-title">{radios('head', HEADER_CHOICES)}</div>
      <input class="note" type="text" id="pick-note" placeholder="Note (optional): anything to change" aria-label="Note about your choice">
      <div><button type="button" class="btn" id="copy">Copy answer</button></div>
      <div id="copy-area" hidden>
        <p class="bar-label">Your browser blocked copying. Select this text and copy it yourself:</p>
        <textarea class="copy-fallback" id="copy-text" readonly aria-label="Your answer as text"></textarea>
      </div>
    </section>
  </main>
</div>

<div class="viewer" id="viewer" hidden role="dialog" aria-modal="true" aria-labelledby="viewer-title">
  <div class="viewer-bar">
    <span class="viewer-title" id="viewer-title"></span>
    <div class="seg" role="group" aria-label="Size">
      <button type="button" id="zoom-fit" aria-pressed="true">Fit</button>
      <button type="button" id="zoom-1" aria-pressed="false">Actual</button>
    </div>
    <span class="viewer-hint">Esc closes</span>
    <button type="button" class="btn ghost" id="viewer-close">Close</button>
  </div>
  <div class="viewer-stage fit" id="viewer-stage"><img id="viewer-img" alt=""></div>
</div>

<script>
(function () {{
  var root = document.documentElement;
  function load(k, f) {{ try {{ var r = localStorage.getItem(k); return r ? JSON.parse(r) : f; }} catch (e) {{ return f; }} }}
  function save(k, v) {{ try {{ localStorage.setItem(k, JSON.stringify(v)); }} catch (e) {{}} }}
  var viewer = document.getElementById('viewer'), stage = document.getElementById('viewer-stage'), img = document.getElementById('viewer-img');
  var state = {{ key: null, title: '', zoom: 'fit', opener: null }};
  var shots = load('cw-timeline3-shots', null);
  if (shots !== 'l' && shots !== 'd') {{
    var dark = root.getAttribute('data-theme') === 'dark' || (root.getAttribute('data-theme') !== 'light' && window.matchMedia && matchMedia('(prefers-color-scheme: dark)').matches);
    shots = dark ? 'd' : 'l';
  }}
  function show() {{
    var source = document.querySelector('.shot[data-key="' + state.key + '"] img.shot-' + shots);
    img.src = source ? source.src : ''; img.alt = state.title;
    document.getElementById('viewer-title').textContent = state.title;
    stage.className = 'viewer-stage ' + (state.zoom === 'fit' ? 'fit' : '');
    document.getElementById('zoom-fit').setAttribute('aria-pressed', String(state.zoom === 'fit'));
    document.getElementById('zoom-1').setAttribute('aria-pressed', String(state.zoom === '1'));
    img.style.width = state.zoom === '1' && img.naturalWidth ? img.naturalWidth + 'px' : '';
  }}
  function setShots(v) {{
    shots = v; root.setAttribute('data-shots', v);
    document.getElementById('shots-l').setAttribute('aria-pressed', String(v === 'l'));
    document.getElementById('shots-d').setAttribute('aria-pressed', String(v === 'd'));
    if (!viewer.hidden) show();
  }}
  document.getElementById('shots-l').addEventListener('click', function () {{ setShots('l'); save('cw-timeline3-shots', 'l'); }});
  document.getElementById('shots-d').addEventListener('click', function () {{ setShots('d'); save('cw-timeline3-shots', 'd'); }});
  document.querySelectorAll('.shot').forEach(function (b) {{
    b.addEventListener('click', function () {{
      state.key = b.getAttribute('data-key'); state.title = b.getAttribute('data-title'); state.opener = b;
      viewer.hidden = false; show(); document.getElementById('viewer-close').focus();
    }});
  }});
  function close() {{ viewer.hidden = true; if (state.opener) state.opener.focus(); }}
  document.getElementById('viewer-close').addEventListener('click', close);
  document.getElementById('zoom-fit').addEventListener('click', function () {{ state.zoom = 'fit'; show(); }});
  document.getElementById('zoom-1').addEventListener('click', function () {{ state.zoom = '1'; show(); }});
  viewer.addEventListener('click', function (e) {{ if (e.target === stage) close(); }});
  document.addEventListener('keydown', function (e) {{ if (!viewer.hidden && e.key === 'Escape') {{ e.preventDefault(); close(); }} }});

  var answer = load('cw-timeline3-answer', {{}});
  var note = document.getElementById('pick-note');
  ['flag', 'head'].forEach(function (group) {{
    document.querySelectorAll('input[name=' + group + ']').forEach(function (r) {{
      if (r.value === answer[group]) r.checked = true;
      r.addEventListener('change', function () {{ answer[group] = r.value; save('cw-timeline3-answer', answer); }});
    }});
  }});
  note.value = answer.note || '';
  note.addEventListener('input', function () {{ answer.note = note.value; save('cw-timeline3-answer', answer); }});
  var flagNames = {{ banner: 'Banner', badge: 'Number badge', arrow: 'Arrow', none: 'none of these' }};
  var headNames = {{ none: 'nothing', name: 'world name', key: 'key to the points', other: 'something else' }};
  var copy = document.getElementById('copy');
  copy.addEventListener('click', function () {{
    var text = 'Save timeline round 3 (#680): flag ' + (answer.flag ? flagNames[answer.flag] : 'not picked') +
      '; above the timeline ' + (answer.head ? headNames[answer.head] : 'not picked') +
      ((answer.note || '').trim() ? ' (' + answer.note.trim() + ')' : '');
    function fallback() {{ var a = document.getElementById('copy-area'), t = document.getElementById('copy-text'); t.value = text; a.hidden = false; t.focus(); t.select(); }}
    if (navigator.clipboard && navigator.clipboard.writeText) {{
      navigator.clipboard.writeText(text).then(function () {{ copy.textContent = 'Copied'; setTimeout(function () {{ copy.textContent = 'Copy answer'; }}, 1800); }}, fallback);
    }} else {{ fallback(); }}
  }});
  setShots(shots);
}})();
</script>
'''
open('../tl2page/index.html', 'w', encoding='utf-8').write(page)
print(round(len(page) / 1e6, 2), 'MB')
