"""Builds the panel review page with every screenshot embedded."""
import base64
import html
import json
from PIL import Image

IMG = 'img'

ITEMS = [
    dict(n=1, name='World Map button', shots=[('mapicon', None)], changes=[
        'The top-left button shows a folded map instead of an envelope. The envelope looked like a messages button.',
    ]),
    dict(n=2, name='Ground label under the pointer', shots=[('hover', None)], changes=[
        'A small swatch of the ground sits beside its name, cut from the same tile art as the map.',
    ]),
    dict(n=3, name='Selected tile card', shots=[('tile', 'Open meadow'), ('tiletree', 'Forest tile with a tree')], changes=[
        'The heading is the ground itself (Meadow, Forest) with a swatch of that tile, instead of “Selected tile”.',
        'Tile position and climate move to one dim line under the heading.',
        'Facts become short labelled rows: Ground, Plants, Height, Weather. Height says Low, Middle or High as well as the number.',
        'Anything on the tile gets its own Here list with its sprite and whether it can be gathered.',
    ]),
    dict(n=4, name='World Map panel', shots=[('worldmap', None)], changes=[
        'Parchment around the map instead of a black box, so it matches the other panels.',
        'Towns show as a small House mark and agents as dots.',
        'A legend underneath: Town, Agent, Your view.',
    ]),
    dict(n=5, name='Map filters and every on/off switch', shots=[('filters', None)], changes=[
        'Each filter has a small picture of what it draws and one line saying what it does. The long note at the bottom goes.',
        'The switch is redrawn as a sunken track with a raised knob. Every switch in the game uses it, so the Settings switches change too.',
    ]),
    dict(n=6, name='Agents list', shots=[('agents', None)], changes=[
        'One row per agent: their map sprite as a portrait, their name, and what they are doing in sentence case.',
        'Tags on the right show who needs attention: Hungry, Cold, Ill.',
        'The header counts the hungry as well as the living.',
        'Click a row to select the agent; double-click or press Enter to open their Profile.',
    ]),
    dict(n=7, name='Event Log', shots=[('events', None)], changes=[
        'Each event has an icon for its kind: a death, food, weather, building, family or the Town.',
        'Each day gets a heading with a rule, so days are easy to tell apart.',
        'Events with a place get a Find button on the right instead of underlined green text.',
        'Events that arrived since you last opened the log keep a small dot (none in this shot).',
    ]),
    dict(n=8, name='Controls list', shots=[('controls', None)], changes=[
        'Keys are drawn as keycaps; mouse actions get a mouse icon.',
        'Grouped into Map, Time and agents, Mouse and Panels, in two columns.',
        'About half the height, so it no longer covers the clock in the top bar.',
    ]),
    dict(n=9, name='World Info: World page', shots=[('world', None)], changes=[
        'A Today card at the top: date and time, the season with its icon, the weather where you are looking, and the year length.',
        'The counts become eight tiles with icons: living agents, Towns, households, buildings, road tiles, bridges, resource sites, map size.',
        'The F1 hint uses a keycap.',
    ]),
    dict(n=10, name='World Info: Towns page', shots=[('towns', None)], changes=[
        'Residents’ portraits under the Town name.',
        'Household stores as item slots with counts, like the building Details panel, instead of a line of text.',
        'Projects get the worker’s portrait, a progress bar with a percentage, and the blocker in red.',
        'Show uses the Find icon instead of the envelope.',
        'The “Playing · date · season · weather” line goes, since the top bar and World page already show it.',
        'Stores and projects scroll inside the panel once it would reach the bottom of the screen.',
    ]),
    dict(n=11, name='Memories and maps', shots=[('memories', None)], changes=[
        'Tabs for All, Memories, Beliefs and Maps, each with a count.',
        'Each entry is a card with an icon for its kind: the text first, then the date and where it came from on one dim line.',
        'How sure the agent is shows as a small five-step meter instead of a percentage.',
        'Beliefs say “Saw it” or “Heard from Ash”. Corrected beliefs get a tag and dim text (none in this shot).',
        'Map entries show each place with a ground swatch and icons of what is there.',
        'Private memories get a Private tag.',
    ]),
    dict(n=12, name='Family tree', fix='Fixes #607', shots=[('family', None)], changes=[
        'Today the panel is blank. The tree is built, but its scroll area comes out zero pixels tall, so nothing shows. I filed this as <a href="https://github.com/compoodment/ClankerWorld/issues/607">#607</a>.',
        'Smaller name boxes with the agent’s portrait. “Living” is dropped; only the dead get “· died”.',
        'A heart marks a partnership. The legend uses the real line colours.',
        'The panel fits the tree instead of filling the screen, and opens beside the Profile.',
        'If you say no to the new look, I will still fix #607 on its own.',
    ]),
    dict(n=13, name='Profile details', shots=[('profile', None)], changes=[
        '“Chosen by OpenAI” joins the Model line instead of taking its own line.',
        'The thoughts preview shows the newest thought on two lines, with no scrollbar.',
        'People become rows with icons: a heart for a partner, Parent of or Child of, Lives with, and Trusts with a small meter.',
        'Suggest and Order sit in one joined switch.',
    ]),
    dict(n=14, name='Agent model settings', fix='Fixes overflow', shots=[('model', None)], changes=[
        'Today the box is wider than the Profile and gets a sideways scrollbar (bottom of the left picture).',
        'Each drop-down gets a caption: Who decides, API key, Model.',
        'Long choices are cut short with “…” instead of widening the box, and the buttons wrap.',
        'On an agent’s page, Refresh and the world-wide Routine and Planning line are hidden. They stay in the world settings.',
        '“Named key saved on host” becomes “This key is saved on the host.” and “Delete named key” becomes “Delete this key”.',
    ]),
    dict(n=15, name='Add an agent', shots=[('addagent', None)], changes=[
        'Captions on the fields: Who decides, API key, Model.',
        'The four-line hint becomes one short line under a divider, with a mouse icon: “Then click on land to place them. Point first to see which household and Town they would join.”',
        'While you point at land it says: “Click to place them here. They would join Reed household in Riverbend.” (not in this shot).',
        'The overlap rules leave the hint. You still get them when you point at a tile where property or borders overlap, with the reason.',
    ]),
    dict(n=16, name='Mod Library', shots=[('mods', None)], changes=[
        'Each mod is a card: a box icon, its name as a heading, who proposed it with their portrait, the version, and a status tag (In use, Proposed).',
        'The internal package name, such as riverbend.pottery, is no longer shown.',
        'A shorter introduction in plain words.',
    ]),
    dict(n=17, name='Confirmation dialogs', fix='Fixes clipped ×', shots=[('delete', 'Delete a world'), ('quit', 'Quit to Main Menu')], changes=[
        'The message is centred under the centred title.',
        'Cancel and the action button sit closer together.',
        'The close button is a framed button inside the frame, level with the title. Today it is a bare × cut off by the frame edge.',
        'World names use straight quotes. The pixel font draws curly quotes full width, which left the gaps around ‘Riverbend’.',
    ]),
    dict(n=18, name='Status messages', shots=[('toast', 'Good news'), ('toastbad', 'A problem')], changes=[
        'A green tick for good news and an amber warning sign for problems.',
        'The text is plain ink instead of green or red, so it reads clearly in both themes.',
        'The box fits the message instead of stretching wide.',
    ]),
    dict(n=19, name='Developer tools', fix='Mock-up', shots=[('devtools', None)], changes=[
        'Today: a Settings page of technical controls (aging override, retry recovery).',
        'Proposed: its own panel, opened with F10, for changing the world while time is paused. Set the weather where you are looking or the season, and place ground, trees, plants, stone, clay, fibre and Houses, or remove things.',
        'The current technical controls move behind “Devices, retries and aging ›”.',
        'The host already supports every one of these changes while paused, so the panel needs no server work.',
        'This picture is a mock-up; the buttons do nothing yet. Ideas for later that need host work: give items, feed or heal an agent, faster time, move an agent.',
    ]),
]


def data_uri(path):
    with open(path, 'rb') as file:
        return 'data:image/png;base64,' + base64.b64encode(file.read()).decode()


def shot_button(item, key, side):
    label = 'Now' if side == 'b' else 'Proposed'
    width = Image.open(f'{IMG}/{key}-l-{side}.png').width
    imgs = ''.join(
        f'<img class="shot-{t}" src="{data_uri(f"{IMG}/{key}-{t}-{side}.png")}" '
        f'width="{width}" alt="{html.escape(item["name"])}, {label.lower()}, {"light" if t == "l" else "dark"} theme">'
        for t in 'ld')
    return (f'<figure class="{"now" if side == "b" else "new"}"><figcaption>{label}</figcaption>'
            f'<button type="button" class="shot" data-key="{key}" data-side="{side}" '
            f'aria-label="Open {html.escape(item["name"])} {label.lower()} full size">{imgs}</button></figure>')


def pair(item, key, caption):
    widths = [Image.open(f'{IMG}/{key}-l-{s}.png').width for s in 'ba']
    layout = 'stack' if max(widths) > 1000 else 'side'
    head = f'<p class="shot-caption">{html.escape(caption)}</p>' if caption else ''
    return f'<div class="pair-wrap">{head}<div class="pair {layout}">{shot_button(item, key, "b")}{shot_button(item, key, "a")}</div></div>'


def item_html(item):
    fix = f'<span class="flag">{html.escape(item["fix"])}</span>' if item.get('fix') else ''
    changes = ''.join(f'<li>{c}</li>' for c in item['changes'])
    pairs = ''.join(pair(item, key, caption) for key, caption in item['shots'])
    n = item['n']
    choices = ''.join(
        f'<label class="choice {cls}"><input type="radio" name="d{n}" id="d{n}-{cls}" value="{cls}"><span>{text}</span></label>'
        for cls, text in (('yes', 'Yes'), ('no', 'No'), ('change', 'Yes, with changes')))
    return f'''
<section class="item" id="item-{n}" data-n="{n}" data-name="{html.escape(item['name'])}">
  <header class="item-head"><span class="num">{n:02d}</span><h2>{html.escape(item['name'])}</h2>{fix}</header>
  <ul class="changes">{changes}</ul>
  {pairs}
  <div class="decide" role="group" aria-label="Your answer for {html.escape(item['name'])}">
    <div class="choices">{choices}</div>
    <input class="note" type="text" id="note-{n}" placeholder="Note (optional): what to change" aria-label="Note for {html.escape(item['name'])}">
  </div>
</section>'''


TEMPLATE = open('template.html', encoding='utf-8').read()
page = TEMPLATE.replace('{{ITEMS}}', ''.join(item_html(i) for i in ITEMS)) \
    .replace('{{COUNT}}', str(len(ITEMS))) \
    .replace('{{NAMES}}', json.dumps({i['n']: i['name'] for i in ITEMS}))
open('index.html', 'w', encoding='utf-8').write(page)
print(round(len(page) / 1e6, 2), 'MB')
