"""Builds the save timeline choice page with every screenshot embedded."""
import base64
import html
import re

STYLE = re.search(r'<style>(.*?)</style>', open('../page/template.html', encoding='utf-8').read(), re.S).group(1)


def uri(name):
    with open(f'img/{name}', 'rb') as file:
        return 'data:image/png;base64,' + base64.b64encode(file.read()).decode()


def shot(key, label, alt):
    imgs = ''.join(f'<img class="shot-{t}" src="{uri(f"{key}-{t}.png")}" alt="{html.escape(alt)}, {"light" if t == "l" else "dark"} theme">' for t in 'ld')
    return (f'<figure><figcaption>{html.escape(label)}</figcaption>'
            f'<button type="button" class="shot" data-key="{key}" data-title="{html.escape(alt)}" aria-label="Open {html.escape(alt)} full size">{imgs}</button></figure>')


OPTIONS = [
    ('A', 'Timeline above the list', [('a', 'The example world'), ('a-busy', 'A busy world: 4 branches, 21 saves, scrolled to now')], [
        'The Load Save panel grows wider, and a timeline sits above the save list you already have.',
        'Each branch is a lane over world days. A line grows down from the save a branch started at.',
        'Click a point to choose that save in the list below; Load and Delete work as today.',
    ], 'You see the whole history and keep the familiar list. The list gets shorter to make room.'),
    ('B', 'Branch lines beside the list', [('b', 'The top of the list'), ('b-scrolled', 'Scrolled down to where branches join')], [
        'No new area: thin coloured lines run down the left edge of the save list, one per branch, like a family tree on its side.',
        'Each save’s dot sits on its branch’s line, and a branch joins the save it started from further down.',
        'The dotted line above Hungry winter is where the world you are playing carries on.',
    ], 'The smallest change, and it scales to any number of saves. It shows the branching, but not when things happened.'),
    ('C', 'Timeline first, list behind a switch', [('c', 'The example world, Before the flood chosen'), ('c-busy', 'A busy world, Second winter chosen')], [
        'The timeline is the main view, larger, with a card under it describing the chosen save: its date, when it was saved, its branch, and what playing on from it will do.',
        'A Timeline / List switch at the top brings back today’s list for anyone who prefers it.',
        'Same points, flags and colours as A.',
    ], 'The most striking, and the clearest about what loading will do. It needs the widest panel.'),
]

LEGEND = [
    ('Filled dot', 'a named save'),
    ('Small ring', 'an autosave'),
    ('Flag', 'the latest save of its branch: playing on from it continues that branch'),
    ('Orange ring', 'the save you have chosen'),
    ('You are here', 'where the world you are playing is now'),
    ('Coloured bands', 'the seasons, named where each one starts, with the day of the season on each tick'),
]


def option_html(letter, title, shots, points, verdict):
    items = ''.join(f'<li>{p}</li>' for p in points)
    figures = ''.join(shot(f'{key}', label, f'Option {letter}, {label}') for key, label in shots)
    return f'''
<section class="item" id="option-{letter}" data-name="Option {letter}">
  <header class="item-head"><span class="num">{letter}</span><h2>{html.escape(title)}</h2></header>
  <ul class="changes">{items}</ul>
  <div class="pair stack">{figures}</div>
  <p class="verdict">{html.escape(verdict)}</p>
</section>'''


legend = ''.join(f'<li><strong>{html.escape(a)}</strong>: {html.escape(b)}</li>' for a, b in LEGEND)
options = ''.join(option_html(*option) for option in OPTIONS)
choices = ''.join(
    f'<label class="choice {cls}"><input type="radio" name="pick" id="pick-{value}" value="{value}"><span>{html.escape(text)}</span></label>'
    for value, cls, text in [('A', 'yes', 'A: above the list'), ('B', 'yes', 'B: beside the list'), ('C', 'yes', 'C: timeline first'), ('none', 'no', 'None of these')])

page = f'''<title>Save Branch Timeline</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Atkinson+Hyperlegible:ital,wght@0,400;0,700;1,400&family=IBM+Plex+Mono:wght@400;600&family=Pixelify+Sans:wght@500;600&display=swap">
<style>{STYLE}
.pair.stack figure {{ justify-items: stretch; }}
.verdict {{ margin: 0; padding: 10px 12px; border-left: 3px solid var(--wood); background: var(--bg); max-width: 78ch; }}
.legend {{ display: grid; gap: 4px; padding-left: 18px; margin: 0; }}
.decide-box {{ background: var(--sheet); border: 1px solid var(--accent); box-shadow: inset 4px 0 0 var(--accent); border-radius: 4px; padding: 18px 20px; display: grid; gap: 12px; margin-block: 18px; }}
.decide-box h2 {{ font: 600 22px/1.2 var(--font-display); margin: 0; }}
</style>

<div class="wrap">
  <header class="intro">
    <p class="eyebrow">ClankerWorld · issue #680 · save branches</p>
    <h1>Save Branch Timeline</h1>
    <p class="lede">You said “we could even make it look cool like a timeline graphic”. These are three ways to draw a world’s save branches in Load Save, each built in the real game on top of the branch work in #682 and the approved art in #629. Every picture uses the example from #679: save <em>Before the flood</em>, play on to <em>Big harvest</em>, then load <em>Before the flood</em> again and play to <em>Hungry winter</em>.</p>
    <ul class="facts"><li>3 options</li><li>1920 × 1080, automatic UI size</li><li>light and dark theme</li></ul>
  </header>

  <section class="notes" aria-label="About these mockups">
    <div class="note-box ask">
      <h2>What I need from you</h2>
      <p>Pick one option at the bottom, or none, and add a note with anything to change. Then press <strong>Copy answer</strong> and paste it into chat. I build nothing until you choose.</p>
    </div>
    <div class="note-box">
      <h2>How to read a timeline</h2>
      <ul class="legend">{legend}</ul>
    </div>
    <div class="note-box">
      <h2>Good to know</h2>
      <ul>
        <li>Whichever you pick, every save stays reachable from the list, as #680 requires.</li>
        <li>A branch sits right under the branch it grew from, newest fork closest, so lines never cross. That is why Branch 3 can sit above Branch 2.</li>
        <li><strong>You are here</strong> needs one small addition: the host must tell the game which branch the running world is on. #682 keeps that record on the host, but the save list does not send it to the game yet.</li>
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
    <section class="item" id="today" data-name="Today">
      <header class="item-head"><span class="num">00</span><h2>Today, with #682</h2></header>
      <ul class="changes"><li>#682 groups saves by branch, tags each card <em>Branch N</em> and marks each branch’s <em>Latest</em> save. That stays the base for every option.</li></ul>
      <div class="pair side">{shot('now', 'Load Save with branches', 'Today’s Load Save list with branches')}</div>
    </section>
    {options}
    <section class="decide-box" aria-labelledby="decide-title">
      <h2 id="decide-title">Your choice</h2>
      <div class="choices" role="radiogroup" aria-labelledby="decide-title">{choices}</div>
      <input class="note" type="text" id="pick-note" placeholder="Note (optional): what to change" aria-label="Note about your choice">
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
  var shots = load('cw-timeline-shots', null);
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
  document.getElementById('shots-l').addEventListener('click', function () {{ setShots('l'); save('cw-timeline-shots', 'l'); }});
  document.getElementById('shots-d').addEventListener('click', function () {{ setShots('d'); save('cw-timeline-shots', 'd'); }});
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

  var answer = load('cw-timeline-answer', {{}});
  var note = document.getElementById('pick-note');
  document.querySelectorAll('input[name=pick]').forEach(function (r) {{
    if (r.value === answer.v) r.checked = true;
    r.addEventListener('change', function () {{ answer.v = r.value; save('cw-timeline-answer', answer); }});
  }});
  note.value = answer.note || '';
  note.addEventListener('input', function () {{ answer.note = note.value; save('cw-timeline-answer', answer); }});
  var names = {{ A: 'A, timeline above the list', B: 'B, branch lines beside the list', C: 'C, timeline first', none: 'none of these' }};
  var copy = document.getElementById('copy');
  copy.addEventListener('click', function () {{
    var text = 'Save timeline (#680): ' + (answer.v ? names[answer.v] : 'no option picked yet') + ((answer.note || '').trim() ? ' (' + answer.note.trim() + ')' : '');
    function fallback() {{ var a = document.getElementById('copy-area'), t = document.getElementById('copy-text'); t.value = text; a.hidden = false; t.focus(); t.select(); }}
    if (navigator.clipboard && navigator.clipboard.writeText) {{
      navigator.clipboard.writeText(text).then(function () {{ copy.textContent = 'Copied'; setTimeout(function () {{ copy.textContent = 'Copy answer'; }}, 1800); }}, fallback);
    }} else {{ fallback(); }}
  }});
  setShots(shots);
}})();
</script>
'''
open('index.html', 'w', encoding='utf-8').write(page)
print(round(len(page) / 1e6, 2), 'MB')
