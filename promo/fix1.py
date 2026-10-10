p = 'promo.js'
s = open(p, encoding='utf-8').read()
BS = chr(92)
rep = [
    ("Tick 146: 220 ore, 14 units" + BS + "nTick 147: 270 ore, 14 units", "Tick 146: 360 ore, 14 units" + BS + "nTick 147: 360 ore, 14 units"),
    ("${win({ x: 980, y: 640, w: 860, h: 330, tabs: [{ name: 'PowerShell', dot: 'b' }], z: 40,", "${win({ x: 860, y: 800, w: 1000, h: 250, tabs: [{ name: 'PowerShell', dot: 'b' }], z: 40,"),
    ("{ at: 2.8, out: `In match 9927ca32…  Watch it at <span class=\"skyc\">${SERVER}/#/match/9927ca32…</span>` },\n      { at: 3.0, out: 'Tick 1: 500 ore, 5 units' }, { at: 3.15, out: 'Tick 2: 500 ore, 5 units' }, { at: 3.3, out: 'Tick 3: 510 ore, 5 units' }, { at: 3.45, out: 'Tick 4: 520 ore, 5 units' },",
     "{ at: 2.8, out: `In match 53135863-ed86-4421-b635-c71784bd7d44. Watch it at <span class=\"skyc\">${SERVER}/#/match/53135863-…</span>` },\n      { at: 3.0, out: 'Tick 0: 500 ore, 5 units' }, { at: 3.15, out: 'Tick 1: 450 ore, 5 units' }, { at: 3.3, out: 'Tick 2: 450 ore, 5 units' }, { at: 3.45, out: 'Tick 3: 450 ore, 5 units' },"),
    ("{ at: 0.1, out: 'Tick 88: 140 ore, 11 units' },", "{ at: 0.1, out: 'Tick 88: 480 ore, 9 units' },"),
    ("{ at: 1.2, out: `In match 4f1e…  Watch it at <span class=\"skyc\">${SERVER}/#/match/4f1e…</span>` },\n      { at: 1.4, out: 'Tick 1: 500 ore, 5 units' },",
     "{ at: 1.2, out: `In match 9927ca32-34b4-466f-8134-323d957dbc30. Watch it at <span class=\"skyc\">${SERVER}/#/match/9927ca32-…</span>` },\n      { at: 1.4, out: 'Tick 0: 500 ore, 5 units' },"),
    ("In match 1c04…  Watch it at <span class=\"skyc\">${SERVER}/#/match/…</span>", "In match 1c042d43-…. Watch it at <span class=\"skyc\">${SERVER}/#/match/1c042d43-…</span>"),
    ("    tags: () => [{ id, kind, label, alpha: prog(0, 0, 1) }],", "    tags: () => [{ id, kind, label }],"),
    ("{ r: 3.4, h: 1.8 }", "{ r: 6.5, h: 3.2 }"), ("{ r: 3.6, h: 1.9 }", "{ r: 6.5, h: 3.2 }"), ("{ r: 3.6, h: 2.0 }", "{ r: 6.5, h: 3.4 }"), ("{ r: 3.8, h: 2.2 }", "{ r: 7, h: 3.6 }"),
    ("{ r: 6.5, h: 4.2, label: 'Noyes Building' }", "{ r: 9, h: 5.5, label: 'Noyes Building' }"), ("{ r: 5.5, h: 3.2 }", "{ r: 8, h: 4.6 }"), ("{ r: 5, h: 3.0 }", "{ r: 8, h: 4.4 }"), ("{ r: 6, h: 3.6 }", "{ r: 9, h: 5 }"),
    ("cam: (u) => ({ ...orbit(7.5, 7.5, lerp(10, 7.5, u), lerp(5, 3.6, u), lerp(40, 80, u), 1.0), fov: 40 }),",
     "cam: (u) => ({ ...orbit(7.5, 7.5, lerp(13, 10, u), lerp(6.5, 5, u), lerp(40, 80, u), 1.4), fov: 40 }),"),
]
for a, b in rep:
    assert a in s, a[:90]
    s = s.replace(a, b)
a = s.index("  add(({ dur }) => shot2d({\n    dur, match: AFTER, tick: (u) => lerp(166, 214, u),")
b = s.index("  cue(at, 'riser');\n  const raiders")
s = s[:a] + """  add(({ dur }) => shot3d({
    dur, match: AFTER, tick: (u) => lerp(172, 192, u),
    cam: (u, tk) => { const c = centroid(AFTER, WORKERS, tk) || { x: 25, z: 25 }; return { pos: [c.x - 9 + u * 2, lerp(9, 7, u), c.z + 6], look: [c.x + 1.5, 0.4, c.z + 1.5], fov: 38 }; },
    tags: () => [69].map((id) => ({ id })),
    caption: (t) => lower(t, '…all the way across the valley.', 'nobody left at home digging grubs', { at: 0.2 }),
    bars: 70,
  }), 3);
""" + s[b:]
open(p, 'w', encoding='utf-8').write(s)

p = 'battle3d.js'
s = open(p, encoding='utf-8').read()
for a, b in [("const white = this.material('#f2f4f7');", "const white = this.material('#c9cfd6');"),
             ("new UnrealBloomPass(new THREE.Vector2(canvas.width, canvas.height), 0.85, 0.55, 0.62)", "new UnrealBloomPass(new THREE.Vector2(canvas.width, canvas.height), 0.85, 0.5, 0.82)")]:
    assert a in s, a
    s = s.replace(a, b)
open(p, 'w', encoding='utf-8').write(s)
print('ok')
