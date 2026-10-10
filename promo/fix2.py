p = 'promo.js'
s = open(p, encoding='utf-8').read()
rep = [
    ("    [DUEL, RT, 104, 'unit', 'Soldier', '<span class=\"mutedc\">110 HP · tough, close-up</span>', { r: 6.5, h: 3.2 }],",
     "    [DUEL, 259, 105, 'unit', 'Soldier', '110 HP · tough, close-up', { r: 6, h: 2.8, deg0: 200, deg1: 240 }],"),
    ("    [DUEL, RT, 109, 'unit', 'Archer', '<span class=\"mutedc\">shoots from four tiles away</span>', { r: 6.5, h: 3.4 }],",
     "    [DUEL, 262, 117, 'unit', 'Archer', 'shoots from four tiles away', { r: 6, h: 3, deg0: 250, deg1: 210 }],"),
    ("add(({ dur }) => rosterShot(dur, m, tick, id, kind, l1, l2, { ...o, deg0: 10 + i * 40, deg1: 55 + i * 40 }), 1.75);",
     "add(({ dur }) => rosterShot(dur, m, tick, id, kind, l1, l2, { deg0: 10 + i * 40, deg1: 55 + i * 40, ...o }), 1.75);"),
    # readable lower thirds: a soft dark scrim behind them
    ("  return `<div class=\"lower\" style=",
     "  return `<div style=\"position:absolute;left:0;right:0;bottom:0;height:420px;background:linear-gradient(transparent,rgba(2,6,12,0.75));opacity:${Math.min(p, q)}\"></div><div class=\"lower\" style="),
]
for a, b in rep:
    assert a in s, a[:90]
    s = s.replace(a, b)
a0 = s.index('const roster = ['); a1 = s.index('];', a0)
s = s[:a0] + s[a0:a1].replace('<span class="mutedc">', '<span>') + s[a1:]
open(p, 'w', encoding='utf-8').write(s)

p = 'promo.html'
s = open(p, encoding='utf-8').read()
a = ".lower .l2 { font-family: var(--code); font-size: 30px; color: var(--sky); margin-top: 10px; text-shadow: 0 2px 16px rgba(0,0,0,0.9); }"
assert a in s
s = s.replace(a, ".lower .l2 { font-family: var(--code); font-size: 32px; font-weight: 600; color: #a9d4ff; margin-top: 12px; text-shadow: 0 2px 12px rgba(0,0,0,1), 0 0 4px rgba(0,0,0,1); }")
open(p, 'w', encoding='utf-8').write(s)

p = 'battle3d.js'
s = open(p, encoding='utf-8').read()
a = "const act = u.activity === 'Attacking' ? `ATTACK → #${u.targetId}`"
assert a in s
s = s.replace(a, "const act = u.activity === 'Attacking' ? (u.targetId == null ? 'ATTACK-MOVE' : `ATTACK → #${u.targetId}`)")
open(p, 'w', encoding='utf-8').write(s)
print('ok')
