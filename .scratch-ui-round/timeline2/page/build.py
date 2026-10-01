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
    ('C', 'The first drawing', [('c', 'The example world, Before the flood chosen'), ('c-busy', 'A busy world, Second winter chosen')], [
        'What you picked last round, unchanged, for comparison.',
        'Thin lines, small hollow dots, names under the dots, often shortened (“Rain at l…”, “Sprin…”).',
    ], 'The layout you liked. The drawing is plain and crowded.'),
    ('1', 'Clean', [('clean', 'The example world, Before the flood chosen'), ('clean-busy', 'A busy world, Second winter chosen')], [
        'Thicker branch lines with a soft shade, and crisp pixel points: an open point with a dot of the branch’s colour, a solid point with a flag for each branch’s newest save, a small diamond for an autosave.',
        'Names sit in small tags, below the line or above it when the space below is taken, so they are almost never shortened. The chosen save’s tag turns orange, with pixel corner marks around its point.',
        'Each season washes the lanes in its own colour, with dotted lines where a season starts and a solid line plus a <em>Year 2</em> tag where a year starts. Days are numbered only where there is room.',
        'Branches are numbered badges with their save count. Where a branch began moves into the card below (“Branch 3 began at Market day”), and the card’s branch tag takes the branch’s colour.',
        '<em>You are here</em> is a small orange camp marker at the end of a dotted line, with its tag beside it so it never covers a name.',
    ], 'My recommendation: easiest to read, and it stays tidy in a busy world.'),
    ('2', 'Trails', [('trails', 'The example world, Before the flood chosen'), ('trails-busy', 'A busy world, Second winter chosen')], [
        'Each branch is a packed-dirt trail like the roads on the map, edged in the branch’s colour, and forks bend off like a side road.',
        'Saves are little wooden signposts with a cap in the branch’s colour; the newest has a flag. Branch names have pennants instead of badges.',
        'Same name tags, season washes, card and <em>You are here</em> as Clean.',
    ], 'The most like the game world. The signposts are small, so it is harder to read at a glance than Clean.'),
]

LEGEND = [
    ('Solid point with a flag', 'the newest save on its branch: playing on from it continues that branch'),
    ('Open point', 'an older named save'),
    ('Small diamond', 'an autosave'),
    ('Orange tag and corner marks', 'the save you have chosen'),
    ('Orange camp marker', 'where the world you are playing is now'),
    ('Coloured wash', 'the season; a solid line and a Year tag mark a new year'),
]


def option_html(letter, title, shots, points, verdict):
    items = ''.join(f'<li>{p}</li>' for p in points)
    figures = ''.join(shot(f'{key}', label, f'{title}, {label}') for key, label in shots)
    return f'''
<section class="item" id="option-{letter.lower()}" data-name="{html.escape(title)}">
  <header class="item-head"><span class="num">{letter}</span><h2>{html.escape(title)}</h2></header>
  <ul class="changes">{items}</ul>
  <div class="pair stack">{figures}</div>
  <p class="verdict">{html.escape(verdict)}</p>
</section>'''


legend = ''.join(f'<li><strong>{html.escape(a)}</strong>: {html.escape(b)}</li>' for a, b in LEGEND)
options = ''.join(option_html(*option) for option in OPTIONS)
choices = ''.join(
    f'<label class="choice {cls}"><input type="radio" name="pick" id="pick-{value}" value="{value}"><span>{html.escape(text)}</span></label>'
    for value, cls, text in [('clean', 'yes', 'Clean'), ('trails', 'yes', 'Trails'), ('first', 'yes', 'Keep the first drawing'), ('none', 'no', 'None of these')])

page = f'''<title>Save Timeline Round 2</title>
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
    <p class="eyebrow">ClankerWorld · issue #680 · save branches · round 2</p>
    <h1>Save Timeline Round 2</h1>
    <p class="lede">You picked C and said “I love C but I think you can do better with the visuals of the timeline”. Here are two new drawings of it, built in the real game the same way as before, beside the first drawing. Only the timeline itself changed; the panel, the Timeline / List switch and the card underneath stay as you approved them.</p>
    <ul class="facts"><li>2 new drawings</li><li>1920 × 1080, automatic UI size</li><li>light and dark theme</li></ul>
  </header>

  <section class="notes" aria-label="About these mockups">
    <div class="note-box ask">
      <h2>What I need from you</h2>
      <p>Pick Clean, Trails, the first drawing, or none, and add a note with anything to change, such as mixing parts of both. Then press <strong>Copy answer</strong> and paste it into chat. Building still waits on #682.</p>
    </div>
    <div class="note-box">
      <h2>How to read a timeline</h2>
      <ul class="legend">{legend}</ul>
    </div>
    <div class="note-box">
      <h2>Good to know</h2>
      <ul>
        <li>Both drawings keep everything from the first one: lanes that never cross, the newest-save flags, clicking a point to choose it, and every save reachable from the list.</li>
        <li><strong>You are here</strong> still needs the host to say which branch the running world is on, as before.</li>
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
  var shots = load('cw-timeline2-shots', null);
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
  document.getElementById('shots-l').addEventListener('click', function () {{ setShots('l'); save('cw-timeline2-shots', 'l'); }});
  document.getElementById('shots-d').addEventListener('click', function () {{ setShots('d'); save('cw-timeline2-shots', 'd'); }});
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

  var answer = load('cw-timeline2-answer', {{}});
  var note = document.getElementById('pick-note');
  document.querySelectorAll('input[name=pick]').forEach(function (r) {{
    if (r.value === answer.v) r.checked = true;
    r.addEventListener('change', function () {{ answer.v = r.value; save('cw-timeline2-answer', answer); }});
  }});
  note.value = answer.note || '';
  note.addEventListener('input', function () {{ answer.note = note.value; save('cw-timeline2-answer', answer); }});
  var names = {{ clean: 'Clean', trails: 'Trails', first: 'keep the first drawing', none: 'none of these' }};
  var copy = document.getElementById('copy');
  copy.addEventListener('click', function () {{
    var text = 'Save timeline C, round 2 (#680): ' + (answer.v ? names[answer.v] : 'no option picked yet') + ((answer.note || '').trim() ? ' (' + answer.note.trim() + ')' : '');
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
