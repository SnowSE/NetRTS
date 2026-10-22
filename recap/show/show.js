// The broadcast. load(show) builds the studio once; seek(t) poses everything for second t. Nothing
// depends on a clock, so the renderer can take exact frames in any order.
'use strict';

(() => {
  const HOSTS = {
    zip: { name: 'Zip', unit: 'The Scout', colour: 'var(--zip)', x: 112, seed: 0.2 },
    sarge: { name: 'Sarge', unit: 'The Soldier', colour: 'var(--sarge)', x: 318, seed: 0.55 },
    quill: { name: 'Quill', unit: 'The Archer', colour: 'var(--quill)', x: 528, seed: 0.85 },
  };
  const ORDER = ['zip', 'sarge', 'quill'];
  const SLOT = ['#e69f00', '#56b4e9', '#2fc497', '#e68fc3', '#ff6b5e', '#b39ddb', '#d4d45a', '#4dd0e1',
    '#bcaaa4', '#f48fb1', '#9fa8da', '#aed581', '#ffab40', '#80cbc4', '#ce93d8', '#90a4ae'];
  const INTRO = 4;
  const OUTRO = 3;

  const $ = (id) => document.getElementById(id);
  const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const num = (n) => Number(n ?? 0).toLocaleString('en-US');
  const same = (a, b) => String(a ?? '').toLowerCase() === String(b ?? '').toLowerCase();

  let show = null;
  let hosts = {};
  let shownGraphic = null;
  let shownLine = -1;
  let tickerWidth = 0;

  window.load = (data) => {
    show = data;
    const b = data.brief;
    const when = new Date(`${b.date}T12:00:00`).toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
    $('logo-game').textContent = b.lore.game;
    $('bug-date').textContent = when;
    $('tc-game').textContent = b.lore.game;
    $('tc-date').textContent = when;
    $('tc-title').textContent = data.title;

    for (const key of ORDER) {
      const h = HOSTS[key];
      // The hosts are portraits of the game's own units (tools/render-hosts.mjs renders them).
      const holder = document.createElement('div');
      holder.className = 'host';
      holder.style.left = `${h.x - 150}px`;
      holder.innerHTML = `<div class="glow" style="--c:${h.colour}"></div><img src="hosts/${key}.png" alt="">`;
      $('hosts').appendChild(holder);
      const plate = document.createElement('div');
      plate.className = 'plate';
      plate.style.setProperty('--c', h.colour);
      plate.innerHTML = `${esc(h.name)}<small>${esc(h.unit)}</small><span class="meter">${'<i></i>'.repeat(7)}</span>`;
      $('plates').appendChild(plate);
      hosts[key] = { holder, img: holder.querySelector('img'), glow: holder.querySelector('.glow'), bars: [...plate.querySelectorAll('.meter i')] };
    }

    // Where each run of lines on the same graphic starts, for transitions and the match clip.
    let runStart = 0;
    data.lines.forEach((l, i) => {
      if (i === 0 || l.graphic !== data.lines[i - 1].graphic) runStart = l.start;
      l.runStart = runStart;
    });

    $('ticker-text').textContent = tickerText(b);
    tickerWidth = $('ticker-text').getBoundingClientRect().width + 80;
    $('ticker-text').textContent = `${tickerText(b)}   ★   ${tickerText(b)}`;
    window.ready = true;
  };

  function tickerText(b) {
    const parts = [`${num(b.totals.matches)} MATCHES`, `${num(b.totals.unitsKilled)} UNITS DOWN`, `${num(b.totals.oreGathered)} ${b.lore.ore.toUpperCase()} GATHERED`,
      `${num(b.totals.botsActive)} BOTS IN ACTION`];
    for (const l of b.ladders) if (l.entries[0]) parts.push(`${l.category.toUpperCase()} LEADER: ${l.entries[0].name}`);
    if (b.newBots.length) parts.push(`WELCOME ${b.newBots.slice(0, 5).map((x) => x.name).join(', ')}`);
    parts.push(b.upcoming.schedule.toUpperCase());
    parts.push('TA MENTORING IN THE COMMONS');
    return parts.join('   ●   ');
  }

  // How open the mouth is for each Azure viseme (0 silence, 21 p/b/m closed ... 2 and 11 wide open).
  const OPENNESS = [0, 0.9, 1, 0.8, 0.75, 0.6, 0.55, 0.6, 0.75, 0.9, 0.7, 0.95, 0.5, 0.55, 0.5, 0.35, 0.45, 0.45, 0.3, 0.35, 0.45, 0.1];

  /** How loud the speaker is at local time s into their line, from its visemes (0 quiet .. 1 loud), smoothed. */
  function loudness(line, s) {
    let level = 0;
    for (const v of line.visemes) {
      if (v.t > s) break;
      level = OPENNESS[v.id] ?? 0.5;
    }
    const tail = Math.max(0, Math.min(1, (line.seconds - s) / 0.15));
    return level * tail;
  }

  /** The line being said at t (or the last one said, between lines). */
  function lineAt(t) {
    let current = -1;
    for (let i = 0; i < show.lines.length; i++) if (show.lines[i].start <= t) current = i;
    return current;
  }

  window.seek = (t) => {
    const intro = Math.max(0, Math.min(1, (INTRO - t) / 0.6));
    const outro = Math.max(0, Math.min(1, (t - (show.duration - OUTRO)) / 0.6));
    const card = Math.max(intro, outro);
    $('titlecard').style.opacity = card;
    $('titlecard').style.display = card > 0 ? 'grid' : 'none';
    $('titlecard').querySelector('.tc-inner').style.transform = `scale(${(1 + 0.06 * Math.min(t, 4) / 4).toFixed(4)})`;
    $('titlecard').querySelector('.tc-stripes').style.transform = `translateX(${((t * 60) % 120).toFixed(1)}px)`;

    const i = lineAt(t);
    const line = show.lines[i];
    const talking = line && t < line.start + line.seconds ? line : null;

    // Hosts: the speaker steps into the light and bobs as they talk, its voice meter moving with the
    // speech; the others idle a little and dim.
    for (const key of ORDER) {
      const p = hosts[key];
      const h = HOSTS[key];
      const me = talking?.speaker === key;
      const voice = me ? loudness(talking, t - talking.start) : 0;
      const lively = me ? (talking.mood === 'excited' || talking.mood === 'shouting' ? 1.6 : talking.mood === 'amused' ? 1.15 : 0.8) : 0.2;
      const bob = Math.abs(Math.sin(t * (me ? 8.3 : 1.2) + h.seed * 5)) * 7 * lively * (me ? 0.35 + voice : 1);
      const tilt = Math.sin(t * (me ? 5.1 : 0.9) + h.seed * 3) * 1.6 * lively;
      p.img.style.transform = `translateY(${(-bob).toFixed(2)}px) rotate(${tilt.toFixed(2)}deg) scale(${me ? 1.05 : 1})`;
      p.glow.style.opacity = me ? (0.75 + voice * 0.25).toFixed(3) : 0;
      p.holder.style.filter = talking && !me ? 'brightness(0.7) saturate(0.85)' : 'none';
      p.bars.forEach((bar, n) => {
        const level = me ? 0.15 + voice * (0.45 + 0.55 * Math.abs(Math.sin(t * (9 + n * 1.7) + n))) : 0.08;
        bar.style.transform = `scaleY(${level.toFixed(3)})`;
      });
    }

    // Lower third: who's talking and what they said.
    const lt = $('lower-third');
    const subject = talking ?? (line && t < line.start + line.seconds + 0.25 ? line : null);
    lt.style.opacity = subject ? 1 : 0;
    if (subject && i !== shownLine) {
      shownLine = i;
      const h = HOSTS[subject.speaker];
      lt.style.setProperty('--c', h.colour);
      $('lt-who').innerHTML = `${esc(h.name)}<small>${esc(h.unit)}</small>`;
      $('lt-said').textContent = subject.text;
    }

    // The big screen.
    const graphic = line?.graphic ?? 'title';
    const key = `${graphic}|${line?.focus ?? ''}|${line?.runStart ?? 0}`;
    if (key !== shownGraphic) {
      shownGraphic = key;
      const [title, html] = screen(graphic, line?.focus ?? '');
      $('screen-title').textContent = title;
      $('screen-body').innerHTML = html;
    }
    const since = line ? t - line.runStart : 0;
    $('screen-body').style.opacity = Math.min(1, since / 0.4).toFixed(3);
    if (graphic === 'match') drawMatch(t - (line?.runStart ?? 0));

    $('ticker-text').style.transform = `translateX(${(-((t * 90) % tickerWidth)).toFixed(1)}px)`;
  };

  // ------------------------------------------------------------------ graphics

  function screen(graphic, focus) {
    const b = show.brief;
    const rows = (items, f) => `<div class="rows">${items.map(f).join('')}</div>`;
    const row = (name, sub, val, isFocus) =>
      `<div class="row${isFocus ? ' focus' : ''}"><div><div class="name">${name}</div>${sub ? `<div class="sub">${sub}</div>` : ''}</div><div class="val">${val ?? ''}</div></div>`;

    switch (graphic) {
      case 'totals':
        return ['By the numbers', `<div class="big-num">
          <div><b>${num(b.totals.matches)}</b><span>matches</span></div>
          <div><b>${num(b.totals.realMatches)}</b><span>bot vs bot</span></div>
          <div><b>${num(b.totals.unitsKilled)}</b><span>units down</span></div>
          <div><b>${num(b.totals.oreGathered)}</b><span>${esc(b.lore.ore)} gathered</span></div>
          <div><b>${num(b.totals.botsActive)}</b><span>bots in action</span></div>
          <div><b>${num(b.totals.longestMatchTicks)}</b><span>ticks, longest match</span></div></div>`];
      case 'newBots':
        return ['New on the field', b.newBots.length
          ? rows(b.newBots.slice(0, 6), (x) => row(esc(x.name), x.owner ? `by ${esc(x.owner)}` : (x.registered ? 'just registered' : ''),
            x.matches ? `${x.wins}-${x.losses}${x.draws ? `-${x.draws}` : ''}` : '<span class="chip new">new</span>', same(x.name, focus)))
          : callout('Welcome', 'No new bots today', 'Yours could be here tomorrow.')];
      case 'match': {
        const m = b.matchOfTheNight;
        if (!m) return ['Match of the night', callout('', 'No match of the night', '')];
        return ['Match of the night', `<div class="match-wrap">
          <div class="match-board">${m.players.slice(0, 8).map((p) =>
            `<span><i class="swatch" style="background:${SLOT[p.slot % 16]}"></i>${p.winner ? '<b>🏆</b>' : ''}${esc(p.name)} <b>${num(p.score)}</b></span>`).join('')}</div>
          <canvas class="map" id="map" width="548" height="300"></canvas></div>
          ${m.why.length ? `<div class="why">${esc(m.why.join(' · '))}</div>` : ''}`];
      }
      case 'powerPlays':
        return ['Power plays', rows(b.powerPlays.slice(0, 5), (x) => row(esc(x.bot), esc(x.text), '', same(x.bot, focus)))];
      case 'surprises':
        return ['Did not see that coming', rows(b.surprises.slice(0, 5), (x) => row(esc(x.bot), esc(x.text), '', same(x.bot, focus)))];
      case 'strategies':
        return ['Strategy watch', rows(b.strategies.slice(0, 6), (x) => row(
          `${esc(x.bot)}`,
          `${x.newOnServer ? '<span class="chip new">first ever</span>' : x.newForBot ? '<span class="chip new">new for them</span>' : ''}${x.usedToPlay ? `used to play ${esc(x.usedToPlay)}` : x.owner ? `by ${esc(x.owner)}` : ''}`,
          `<span class="chip">${esc(x.strategy)}</span>`, same(x.bot, focus)))];
      case 'ladder': {
        const ladder = b.ladders.find((l) => same(l.category, focus)) ?? b.ladders.find((l) => l.category === 'Season') ?? b.ladders[0];
        if (!ladder) return ['The ladder', callout('', 'No ladder yet', '')];
        return [`${ladder.category} ladder`, `<table class="ladder">${ladder.entries.slice(0, 10).map((e) => {
          const mv = e.previousRank == null ? '<span class="up">NEW</span>' : e.previousRank > e.rank ? `<span class="up">▲${e.previousRank - e.rank}</span>`
            : e.previousRank < e.rank ? `<span class="down">▼${e.rank - e.previousRank}</span>` : '<span class="same">–</span>';
          return `<tr class="${same(e.name, focus) ? 'focus' : ''}"><td class="r">${e.rank}</td><td class="mv">${mv}</td><td class="n">${esc(e.name)}</td>
            <td class="o">${esc(e.owner ?? '')}</td><td class="v">${ladder.rankedBy === 'points' ? `${num(e.points)} pts` : num(e.rating)}</td></tr>`;
        }).join('')}</table>`];
      }
      case 'movers':
        return ['On the move', rows(b.movers.slice(0, 7), (x) => row(esc(x.name), `${esc(x.category)} · ${x.wins}-${x.losses} today`,
          `<span class="${x.to > x.from ? 'up' : 'down'}">${x.to > x.from ? '▲' : '▼'} ${Math.abs(x.to - x.from)}</span> <span class="sub">${x.to}</span>`, same(x.name, focus)))];
      case 'achievements':
        return ['Achievements unlocked', rows(b.achievements.slice(0, 6), (x) => row(
          `<span class="chip ${x.tier}">${x.tier}</span>${esc(x.name)}`, `${esc(x.bot)}${x.firstEver ? ' · <b style="color:var(--gold)">FIRST EVER</b>' : ''}`,
          '', same(x.bot, focus)))];
      case 'rivalries':
        return ['Rivalry watch', `<div class="rows">${b.rivalries.slice(0, 5).map((r) =>
          `<div class="vs"><span>${esc(r.a)}</span><span class="mid">${r.aWins} – ${r.bWins}${r.draws ? ` (${r.draws})` : ''}</span><span class="b">${esc(r.b)}</span></div>`).join('')}</div>`];
      case 'missing':
        return ['Missing persons', `<div class="poster-grid">${b.missingPersons.slice(0, 3).map((x, k) =>
          `<div class="poster" style="--rot:${[-3, 2, -1][k]}deg"><div class="h">MISSING</div><div class="n">${esc(x.name)}</div>
            <div class="d">Last seen ${x.daysAway} days ago<br>${num(x.matches)} matches played${x.owner ? `<br>Owner: ${esc(x.owner)}` : ''}</div></div>`).join('')}</div>`];
      case 'upcoming': {
        const u = b.upcoming;
        return ['Coming up', `<div class="callout"><div class="k">League play</div><div class="t">${esc(u.schedule)}</div>
          ${u.nextLeagueDay ? `<div class="s">Next: ${esc(u.nextLeagueDay)}</div>` : ''}
          ${u.leagueMatches.length ? `<div class="s">Taking players now: ${u.leagueMatches.map((l) => `${esc(l.name)} (${l.joined} in)`).join(', ')}</div>` : ''}</div>`];
      }
      case 'mentoring':
        return ['Get some help', callout('TA mentoring', 'Stuck? The TAs can help.', esc(b.mentoring))];
      default:
        return [b.lore.game, callout(b.weekday, esc(show.title), `${num(b.totals.matches)} matches · ${num(b.totals.botsActive)} bots`)];
    }
  }

  function callout(kicker, title, sub) {
    return `<div class="callout">${kicker ? `<div class="k">${esc(kicker)}</div>` : ''}<div class="t">${title}</div>${sub ? `<div class="s">${sub}</div>` : ''}</div>`;
  }

  /** Where the soldiers, archers and scouts were during the clip, padded, in the canvas's shape. */
  function actionArea(clip, aspect) {
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const f of clip.frames) {
      for (const [x, y, , type] of f.units) {
        if (type === 0) continue;
        x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y);
      }
    }
    if (x0 === Infinity) return { x: 0, y: 0, w: clip.width, h: clip.height };
    let w = Math.max(20, x1 - x0 + 9), h = Math.max(20 / aspect, y1 - y0 + 9);
    if (w / h < aspect) w = h * aspect; else h = w / aspect;
    if (w >= clip.width || h >= clip.height) return { x: 0, y: 0, w: clip.width, h: clip.height };
    const cx = (x0 + x1 + 1) / 2, cy = (y0 + y1 + 1) / 2;
    return { x: Math.max(0, Math.min(clip.width - w, cx - w / 2)), y: Math.max(0, Math.min(clip.height - h, cy - h / 2)), w, h };
  }

  /** The match of the night's busiest stretch, at 8 ticks a second, looping. */
  function drawMatch(seconds) {
    const canvas = $('map');
    const clip = show.clip;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#0c1b26';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    if (!clip) {
      ctx.fillStyle = '#9fb3c2';
      ctx.font = '20px sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText('Replay unavailable', canvas.width / 2, canvas.height / 2);
      return;
    }
    const frame = clip.frames[Math.floor(Math.max(0, seconds) * 8) % clip.frames.length];
    const view = clip.view ??= actionArea(clip, canvas.width / canvas.height);
    const cell = Math.min(canvas.width / view.w, canvas.height / view.h);
    const ox = (canvas.width - cell * view.w) / 2 - view.x * cell;
    const oy = (canvas.height - cell * view.h) / 2 - view.y * cell;
    ctx.fillStyle = '#132a3a';
    ctx.fillRect(ox, oy, cell * clip.width, cell * clip.height);
    ctx.fillStyle = '#3c5463';
    clip.terrain.forEach((rowText, y) => {
      for (let x = 0; x < rowText.length; x++) if (rowText[x] === '#') ctx.fillRect(ox + x * cell, oy + y * cell, cell + 0.3, cell + 0.3);
    });
    ctx.fillStyle = '#c99a10';
    for (const [x, y] of frame.resources) ctx.fillRect(ox + x * cell + cell * 0.2, oy + y * cell + cell * 0.2, cell * 0.6, cell * 0.6);
    for (const [x, y, owner, type, hp, done] of frame.buildings) {
      const size = type === 'CommandCenter' ? 3 : 2;
      ctx.globalAlpha = done ? 1 : 0.5;
      ctx.fillStyle = SLOT[owner % 16];
      ctx.fillRect(ox + (x + 0.5 - size / 2) * cell, oy + (y + 0.5 - size / 2) * cell, size * cell, size * cell);
      ctx.fillStyle = hp < 0.35 ? '#ff4b3e' : '#ffffff';
      ctx.fillRect(ox + (x + 0.5 - size / 2) * cell, oy + (y + 0.5 + size / 2) * cell, size * cell * hp, Math.max(1.5, cell * 0.25));
    }
    ctx.globalAlpha = 1;
    for (const [x, y, owner, type] of frame.units) {
      ctx.fillStyle = SLOT[owner % 16];
      ctx.beginPath();
      ctx.arc(ox + (x + 0.5) * cell, oy + (y + 0.5) * cell, cell * (type === 0 ? 0.45 : 0.7), 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.fillStyle = '#eef3f6';
    ctx.font = 'bold 15px sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText(`TICK ${frame.tick}`, 10, 20);
  }
})();
