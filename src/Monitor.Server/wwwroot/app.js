'use strict';

// Painel do Monitor Portoconseg. JavaScript sem bibliotecas; todo texto vindo do servidor passa por esc().

const TZ = 'America/Sao_Paulo';
const CATEGORY_NAMES = { work: 'Trabalho', personal: 'Pessoal', neutral: 'Neutro', unclassified: 'Não classificado' };
const STATE_NAMES = { idle: 'Inativo', locked: 'Tela bloqueada', off: 'Sem registro (desligado, suspenso ou sem o programa)' };
const DAY_NAMES = [
  ['monday', 'Segunda'], ['tuesday', 'Terça'], ['wednesday', 'Quarta'], ['thursday', 'Quinta'],
  ['friday', 'Sexta'], ['saturday', 'Sábado'], ['sunday', 'Domingo'],
];
const ACTION_NAMES = {
  login: 'Entrou', login_falhou: 'Senha errada', logout: 'Saiu', senha_alterada: 'Trocou a senha',
  computador_criado: 'Cadastrou computador', computador_alterado: 'Alterou computador', chave_regerada: 'Gerou nova chave',
  viu_relatorio_dia: 'Viu relatório do dia', viu_relatorio_semana: 'Viu fechamento da semana',
  regra_salva: 'Classificou', regra_removida: 'Removeu classificação', config_alterada: 'Alterou configuração',
  horario_salvo: 'Salvou horário', horario_removido: 'Removeu horário', baixou_instalador: 'Baixou o instalador',
};

const state = { day: null, week: null, me: null, refreshTimer: null };
const main = () => document.getElementById('main');

// ---------- utilitários ----------

function esc(value) {
  return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

async function api(method, path, body) {
  const headers = { 'X-Requested-With': 'monitor' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch('/api' + path, {
    method, headers, credentials: 'same-origin',
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  let data = null;
  try { data = await res.json(); } catch { /* resposta sem corpo */ }
  if (res.status === 401 && path !== '/login') {
    showLogin();
    throw new Error('Sessão encerrada. Entre de novo.');
  }
  if (!res.ok) throw new Error((data && data.error) || `Erro ${res.status}`);
  return data;
}

const timeFmt = new Intl.DateTimeFormat('pt-BR', { timeZone: TZ, hour: '2-digit', minute: '2-digit' });
const dateTimeFmt = new Intl.DateTimeFormat('pt-BR', { timeZone: TZ, day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
const dayFmt = new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC', weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' });
const dayShortFmt = new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC', weekday: 'short', day: '2-digit', month: '2-digit' });
const isoFmt = new Intl.DateTimeFormat('en-CA', { timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit' });

const fmtTime = ms => (ms == null ? '—' : timeFmt.format(new Date(ms)));
const fmtDateTime = ms => (ms == null ? '—' : dateTimeFmt.format(new Date(ms)));
const fmtDay = iso => dayFmt.format(new Date(iso + 'T12:00:00Z'));
const fmtDayShort = iso => dayShortFmt.format(new Date(iso + 'T12:00:00Z')).replace(',', '');
const today = () => isoFmt.format(new Date());

function addDays(iso, n) {
  const d = new Date(iso + 'T12:00:00Z');
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

function fmtDur(ms) {
  const totalMin = Math.round((ms || 0) / 60000);
  if (totalMin < 60) return `${totalMin} min`;
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return `${h}h${String(m).padStart(2, '0')}`;
}

const pct = (part, whole) => (whole > 0 ? `${Math.round((part / whole) * 100)}% do horário` : '');

function toast(message) {
  const el = document.createElement('div');
  el.className = 'toast';
  el.textContent = message;
  document.body.appendChild(el);
  setTimeout(() => el.remove(), 3000);
}

function showError(err) {
  if (err && err.message) toast(err.message);
}

// ---------- login ----------

function showLogin() {
  clearInterval(state.refreshTimer);
  document.getElementById('app-view').hidden = true;
  document.getElementById('login-view').hidden = false;
  document.getElementById('login-user').focus();
}

function showApp() {
  document.getElementById('login-view').hidden = true;
  document.getElementById('app-view').hidden = false;
  document.getElementById('user-name').textContent = state.me ? state.me.displayName : '';
  route();
}

document.getElementById('login-form').addEventListener('submit', async e => {
  e.preventDefault();
  const errorEl = document.getElementById('login-error');
  errorEl.hidden = true;
  try {
    state.me = await api('POST', '/login', {
      username: document.getElementById('login-user').value,
      password: document.getElementById('login-pass').value,
    });
    document.getElementById('login-pass').value = '';
    showApp();
  } catch (err) {
    errorEl.textContent = err.message;
    errorEl.hidden = false;
  }
});

document.getElementById('logout').addEventListener('click', async () => {
  try { await api('POST', '/logout'); } catch { /* já saiu */ }
  state.me = null;
  showLogin();
});

// ---------- rotas ----------

const routes = { dia: renderDay, semana: renderWeek, classificar: renderClassify, computadores: renderMachines, horarios: renderSchedules, acessos: renderAudit };

function route() {
  clearInterval(state.refreshTimer);
  const name = (location.hash || '#dia').slice(1);
  const page = routes[name] ? name : 'dia';
  document.querySelectorAll('#nav a').forEach(a => a.classList.toggle('active', a.dataset.route === page));
  main().innerHTML = '<p class="muted">Carregando…</p>';
  routes[page]().catch(err => {
    main().innerHTML = `<div class="callout warn">${esc(err.message)}</div>`;
  });
}
window.addEventListener('hashchange', route);

// ---------- Dia ----------

async function renderDay() {
  state.day = state.day || today();
  const data = await api('GET', `/report/day?date=${state.day}`);
  const isToday = data.date === today();
  const scheduleText = data.schedule ? `Horário combinado: ${data.schedule.start} às ${data.schedule.end}` : 'Dia sem expediente combinado';

  let html = `
    <div class="toolbar">
      <h1>Dia</h1>
      <button id="d-prev" aria-label="Dia anterior">◀</button>
      <input id="d-date" type="date" value="${esc(data.date)}">
      <button id="d-next" aria-label="Próximo dia">▶</button>
      <button id="d-today">Hoje</button>
    </div>
    <p class="note">${esc(fmtDay(data.date))} · ${esc(scheduleText)} · Inativo = ${data.idleMinutes} min ou mais sem teclado e mouse.
    ${isToday ? ` Os dados chegam com cerca de ${data.idleMinutes + 1} minutos de atraso. Atualizado às ${fmtTime(data.generatedMs)}.` : ''}</p>`;

  if (data.machines.length === 0) {
    html += `<div class="card empty">Nenhum computador cadastrado. Cadastre em <a href="#computadores">Computadores</a>.</div>`;
  }
  for (const item of data.machines) html += dayCard(item.machine, item.report, data);
  main().innerHTML = html;

  document.getElementById('d-prev').onclick = () => { state.day = addDays(state.day, -1); route(); };
  document.getElementById('d-next').onclick = () => { state.day = addDays(state.day, 1); route(); };
  document.getElementById('d-today').onclick = () => { state.day = today(); route(); };
  document.getElementById('d-date').onchange = e => { if (e.target.value) { state.day = e.target.value; route(); } };

  if (isToday) state.refreshTimer = setInterval(() => { if (location.hash === '' || location.hash === '#dia') route(); }, 120000);
}

function machineTitle(m) {
  return `<h2 class="who">${esc(m.name)}${m.employee ? ` <span class="muted">· ${esc(m.employee)}</span>` : ''}</h2>
    <span class="status"><span class="dot ${m.online ? 'on' : ''}"></span>${m.online ? 'Programa conectado' : m.lastSeenMs ? `Último contato ${esc(fmtDateTime(m.lastSeenMs))}` : 'Nunca conectou'}</span>`;
}

function dayCard(machine, report, data) {
  const s = report.summary;
  const hasData = report.timeline.length > 0;
  let html = `<section class="card"><div class="card-head">${machineTitle(machine)}</div>`;
  if (!hasData) {
    return html + `<p class="muted">Nenhum registro neste dia.</p></section>`;
  }

  html += `<div class="stats">
    <div class="stat"><span class="label">Chegada</span><span class="value">${fmtTime(s.arrivalMs)}</span><span class="sub">primeira atividade</span></div>
    <div class="stat"><span class="label">Saída</span><span class="value">${fmtTime(s.departureMs)}</span><span class="sub">última atividade</span></div>
    <div class="stat k-active"><span class="label">Ativo</span><span class="value">${fmtDur(s.activeMs)}</span><span class="sub">${pct(s.activeMs, s.windowElapsedMs)}</span></div>
    <div class="stat k-idle"><span class="label">Inativo</span><span class="value">${fmtDur(s.idleMs)}</span><span class="sub">${pct(s.idleMs, s.windowElapsedMs)}</span></div>
    <div class="stat k-away"><span class="label">Fora do computador</span><span class="value">${fmtDur(s.awayMs)}</span><span class="sub">bloqueado, desligado ou ausente</span></div>
    <div class="stat k-personal"><span class="label">Uso pessoal</span><span class="value">${fmtDur(s.personalMs)}</span><span class="sub">dentro do horário</span></div>
    <div class="stat"><span class="label">Fora do horário</span><span class="value">${fmtDur(s.outsideHoursActiveMs)}</span><span class="sub">ativo antes ou depois</span></div>
  </div>`;

  html += timelineHtml(report, s);

  if (report.top.length > 0) {
    const max = report.top[0].ms;
    html += `<div class="top"><h3>O que foi feito no horário</h3>` + report.top.slice(0, 12).map(t => `
      <div class="top-row">
        <span class="name" title="${esc(t.label)}">${esc(t.label)} <span class="chip c-${esc(t.category)}">${esc(CATEGORY_NAMES[t.category] || t.category)}</span></span>
        <span class="bar"><span style="width:${Math.max(2, (t.ms / max) * 100)}%;background:var(--${esc(t.category)})"></span></span>
        <span class="num">${fmtDur(t.ms)}</span>
      </div>`).join('') + `</div>`;
  }

  html += `<details><summary>Registro detalhado (${report.log.length} trechos)</summary>
    <div class="tbl" style="margin-top:10px"><table>
      <thead><tr><th>Início</th><th>Fim</th><th>Duração</th><th>Programa ou site</th><th>Janela</th></tr></thead>
      <tbody>${report.log.map(l => `
        <tr>
          <td class="t">${fmtTime(l.startMs)}</td>
          <td class="t">${fmtTime(l.endMs)}</td>
          <td class="t">${fmtDur(l.endMs - l.startMs)}</td>
          <td>${l.state === 'active'
            ? `${esc(l.label)} <span class="chip c-${esc(l.category)}">${esc(CATEGORY_NAMES[l.category] || '')}</span>`
            : `<span class="muted">${esc(STATE_NAMES[l.state] || l.state)}</span>`}</td>
          <td class="title">${esc(l.title || '')}</td>
        </tr>`).join('')}</tbody>
    </table></div></details>`;

  return html + `</section>`;
}

function timelineHtml(report, s) {
  const segs = report.timeline;
  let from = segs[0].startMs;
  let to = segs[segs.length - 1].endMs;
  if (s.windowStartMs) from = Math.min(from, s.windowStartMs);
  if (s.windowEndMs) to = Math.max(to, s.windowEndMs);
  const hour = 3600000;
  from = Math.floor(from / hour) * hour;
  to = Math.ceil(to / hour) * hour;
  const span = Math.max(to - from, hour);
  const pos = ms => ((ms - from) / span) * 100;

  let track = '';
  if (s.windowStartMs && s.windowEndMs) {
    track += `<div class="tl-window" style="left:${pos(s.windowStartMs)}%;width:${pos(s.windowEndMs) - pos(s.windowStartMs)}%"></div>`;
  }
  for (const seg of segs) {
    if (seg.state === 'off' || seg.state === 'locked') continue;
    const cls = seg.state === 'idle' ? 's-idle' : `c-${seg.category}`;
    const label = seg.state === 'active' ? seg.label : STATE_NAMES[seg.state];
    track += `<div class="tl-seg ${esc(cls)}" style="left:${pos(seg.startMs)}%;width:${Math.max(0.15, pos(seg.endMs) - pos(seg.startMs))}%"
      title="${esc(fmtTime(seg.startMs))}–${esc(fmtTime(seg.endMs))} ${esc(label)}"></div>`;
  }

  let axis = '';
  const hours = Math.round(span / hour);
  const step = hours > 14 ? 2 : 1;
  for (let i = 0; i <= hours; i += step) {
    const t = from + i * hour;
    axis += `<span style="left:${pos(t)}%">${fmtTime(t).slice(0, 2)}h</span>`;
  }

  return `<div class="timeline">
    <div class="tl-track">${track}</div>
    <div class="tl-axis">${axis}</div>
    <div class="legend">
      <span><i style="background:var(--work)"></i>Trabalho</span>
      <span><i style="background:var(--unclassified)"></i>Não classificado</span>
      <span><i style="background:var(--neutral)"></i>Neutro</span>
      <span><i style="background:var(--personal)"></i>Pessoal</span>
      <span><i style="background:repeating-linear-gradient(45deg,var(--idle) 0 3px,transparent 3px 6px)"></i>Inativo</span>
      <span><i style="background:var(--locked);border:1px solid var(--line)"></i>Fora do computador</span>
      <span>Linhas verticais: horário combinado</span>
    </div>
  </div>`;
}

// ---------- Semana ----------

async function renderWeek() {
  // Na segunda-feira o padrão é o fechamento da semana que acabou.
  if (!state.week) state.week = new Date(today() + 'T12:00:00Z').getUTCDay() === 1 ? addDays(today(), -7) : today();
  const data = await api('GET', `/report/week?date=${state.week}`);
  const end = data.days[data.days.length - 1];

  let html = `
    <div class="toolbar">
      <h1>Fechamento da semana</h1>
      <button id="w-prev" aria-label="Semana anterior">◀</button>
      <span>${esc(fmtDayShort(data.weekStart))} a ${esc(fmtDayShort(end))}</span>
      <button id="w-next" aria-label="Próxima semana">▶</button>
      <button id="w-this">Esta semana</button>
      <button id="w-last">Semana passada</button>
    </div>
    <p class="note">Ativo, inativo, fora do computador e uso pessoal contam só dentro do horário combinado de cada dia.</p>`;

  if (data.machines.length === 0) html += `<div class="card empty">Nenhum computador cadastrado.</div>`;

  for (const item of data.machines) {
    const rows = item.days.filter(d => d.workDay || d.activeMs > 0 || d.outsideHoursActiveMs > 0);
    const t = item.totals;
    html += `<section class="card"><div class="card-head">${machineTitle(item.machine)}</div>
      <div class="tbl"><table>
        <thead><tr><th>Dia</th><th>Chegada</th><th>Saída</th><th>Ativo</th><th>Inativo</th><th>Fora do PC</th><th>Pessoal</th><th>Fora do horário</th></tr></thead>
        <tbody>
          ${rows.map(d => `<tr>
            <td style="white-space:nowrap"><a href="#dia" data-day="${esc(d.date)}">${esc(fmtDayShort(d.date))}</a>${d.workDay ? '' : ' <span class="muted">(sem expediente)</span>'}</td>
            <td class="t">${fmtTime(d.arrivalMs)}</td>
            <td class="t">${fmtTime(d.departureMs)}</td>
            <td class="t">${fmtDur(d.activeMs)} <span class="muted">${d.windowElapsedMs ? Math.round(d.activeMs / d.windowElapsedMs * 100) + '%' : ''}</span></td>
            <td class="t">${fmtDur(d.idleMs)}</td>
            <td class="t">${fmtDur(d.awayMs)}</td>
            <td class="t">${fmtDur(d.personalMs)}</td>
            <td class="t">${fmtDur(d.outsideHoursActiveMs)}</td>
          </tr>`).join('')}
          <tr class="total">
            <td>Total</td><td></td><td></td>
            <td class="t">${fmtDur(t.activeMs)} <span class="muted">${t.windowElapsedMs ? Math.round(t.activeMs / t.windowElapsedMs * 100) + '%' : ''}</span></td>
            <td class="t">${fmtDur(t.idleMs)}</td>
            <td class="t">${fmtDur(t.awayMs)}</td>
            <td class="t">${fmtDur(t.personalMs)}</td>
            <td class="t">${fmtDur(t.outsideHoursActiveMs)}</td>
          </tr>
        </tbody>
      </table></div></section>`;
  }
  main().innerHTML = html;

  document.getElementById('w-prev').onclick = () => { state.week = addDays(data.weekStart, -7); route(); };
  document.getElementById('w-next').onclick = () => { state.week = addDays(data.weekStart, 7); route(); };
  document.getElementById('w-this').onclick = () => { state.week = today(); route(); };
  document.getElementById('w-last').onclick = () => { state.week = addDays(today(), -7); route(); };
  main().querySelectorAll('a[data-day]').forEach(a => a.addEventListener('click', () => { state.day = a.dataset.day; }));
}

// ---------- Classificar ----------

async function renderClassify() {
  const [pending, rules] = await Promise.all([api('GET', '/unclassified?days=30'), api('GET', '/rules')]);

  let html = `
    <div class="toolbar"><h1>Classificar programas e sites</h1></div>
    <p class="note">Marcar como <b>pessoal</b> apaga o nome do site, o programa e o título da janela já guardados: fica só o tempo, como "Uso pessoal".
    <b>Neutro</b> serve para coisas do Windows, como a área de trabalho.</p>
    <section class="card"><h2>Não classificados nos últimos 30 dias</h2>`;

  if (pending.length === 0) {
    html += `<p class="muted">Nada pendente.</p>`;
  } else {
    html += `<div class="tbl"><table>
      <thead><tr><th>Tipo</th><th>Nome</th><th>Tempo</th><th>Exemplos de janela</th><th>Nome no relatório</th><th>Classificar</th></tr></thead>
      <tbody>${pending.map((p, i) => `<tr>
        <td>${p.kind === 'domain' ? 'Site' : 'Programa'}</td>
        <td><b>${esc(p.label)}</b>${p.kind === 'process' ? `<br><span class="muted num">${esc(p.pattern)}</span>` : ''}</td>
        <td class="t">${fmtDur(p.ms)}</td>
        <td class="title">${p.titles.map(esc).join('<br>')}</td>
        <td><input id="pl-${i}" placeholder="${esc(p.label)}" aria-label="Nome no relatório"></td>
        <td style="white-space:nowrap">
          <button class="small cat-work" data-i="${i}" data-cat="work">Trabalho</button>
          <button class="small cat-personal" data-i="${i}" data-cat="personal">Pessoal</button>
          <button class="small cat-neutral" data-i="${i}" data-cat="neutral">Neutro</button>
        </td>
      </tr>`).join('')}</tbody></table></div>`;
  }
  html += `</section>`;

  html += `<section class="card"><h2>Adicionar regra</h2>
    <form id="r-form" class="form-row">
      <label>Tipo <select id="r-kind"><option value="domain">Site</option><option value="process">Programa</option></select></label>
      <label>Endereço ou programa <input id="r-pattern" placeholder="ex.: ploomes.com ou segflexnet" required></label>
      <label>Nome no relatório <input id="r-label" placeholder="ex.: Ploomes"></label>
      <label>Categoria <select id="r-cat"><option value="work">Trabalho</option><option value="personal">Pessoal</option><option value="neutral">Neutro</option></select></label>
      <button class="primary" type="submit">Salvar</button>
    </form>
    <p class="note">Um site vale também para os subdomínios: "ploomes.com" inclui "app.ploomes.com".</p>
  </section>`;

  html += `<section class="card"><h2>Regras (${rules.length})</h2>
    <div class="form-row"><label>Mostrar <select id="r-filter">
      <option value="">Todas</option><option value="work">Trabalho</option><option value="personal">Pessoal</option><option value="neutral">Neutro</option>
    </select></label></div>
    <div class="tbl"><table>
      <thead><tr><th>Tipo</th><th>Endereço ou programa</th><th>Nome no relatório</th><th>Categoria</th><th></th></tr></thead>
      <tbody id="r-body">${rules.map(r => `<tr data-cat="${esc(r.category)}">
        <td>${r.kind === 'domain' ? 'Site' : 'Programa'}</td>
        <td class="num">${esc(r.pattern)}</td>
        <td>${esc(r.label || '')}</td>
        <td><span class="chip c-${esc(r.category)}">${esc(CATEGORY_NAMES[r.category] || r.category)}</span></td>
        <td><button class="small" data-del="${r.id}">Remover</button></td>
      </tr>`).join('')}</tbody>
    </table></div></section>`;

  main().innerHTML = html;

  main().querySelectorAll('button[data-cat]').forEach(btn => btn.addEventListener('click', async () => {
    const p = pending[Number(btn.dataset.i)];
    const label = document.getElementById(`pl-${btn.dataset.i}`).value.trim() || (p.kind === 'process' ? p.label : '');
    try {
      await api('POST', '/rules', { kind: p.kind, pattern: p.pattern, category: btn.dataset.cat, label });
      toast(`${p.label}: ${CATEGORY_NAMES[btn.dataset.cat]}`);
      route();
    } catch (err) { showError(err); }
  }));

  document.getElementById('r-form').addEventListener('submit', async e => {
    e.preventDefault();
    try {
      await api('POST', '/rules', {
        kind: document.getElementById('r-kind').value,
        pattern: document.getElementById('r-pattern').value,
        label: document.getElementById('r-label').value,
        category: document.getElementById('r-cat').value,
      });
      toast('Regra salva');
      route();
    } catch (err) { showError(err); }
  });

  document.getElementById('r-filter').addEventListener('change', e => {
    document.querySelectorAll('#r-body tr').forEach(tr => { tr.hidden = e.target.value !== '' && tr.dataset.cat !== e.target.value; });
  });

  main().querySelectorAll('button[data-del]').forEach(btn => btn.addEventListener('click', async () => {
    if (!confirm('Remover esta regra? O que ela classificava volta a aparecer como "não classificado".')) return;
    try { await api('DELETE', `/rules/${btn.dataset.del}`); route(); } catch (err) { showError(err); }
  }));
}

// ---------- Computadores ----------

async function renderMachines() {
  const machines = await api('GET', '/machines');
  let html = `
    <div class="toolbar"><h1>Computadores</h1></div>
    <section class="card"><h2>Como instalar num computador</h2>
      <ol style="margin:0;padding-left:20px;display:flex;flex-direction:column;gap:4px">
        <li>Cadastre o computador abaixo e copie a chave.</li>
        <li>No computador do funcionário, entre neste painel e baixe o instalador: <a href="/api/download/agente">MonitorPortoconseg-instalador.zip</a>.</li>
        <li>Extraia o .zip, clique com o botão direito em <b>instalar.bat</b> e escolha <b>Executar como administrador</b>.</li>
        <li>Informe o endereço do painel e a chave. Em cerca de 1 minuto o computador aparece como conectado.</li>
      </ol>
      <p class="note">Se aparecer "O Windows protegeu o computador", clique em "Mais informações" e depois em "Executar assim mesmo". O aviso aparece porque o programa não tem certificado de assinatura pago.</p>
    </section>
    <section class="card"><h2>Cadastrar computador</h2>
      <form id="m-form" class="form-row">
        <label>Nome <input id="m-name" placeholder="ex.: Computador 1" required></label>
        <label>Funcionário <input id="m-employee" placeholder="ex.: Maria"></label>
        <button class="primary" type="submit">Cadastrar e gerar chave</button>
      </form>
      <div id="m-token"></div>
    </section>
    <section class="card"><h2>Cadastrados</h2>`;

  if (machines.length === 0) {
    html += `<p class="muted">Nenhum computador ainda.</p>`;
  } else {
    html += `<div class="tbl"><table>
      <thead><tr><th>Nome</th><th>Funcionário</th><th>Situação</th><th>Windows</th><th>Versão</th><th></th></tr></thead>
      <tbody>${machines.map(m => `<tr>
        <td><b>${esc(m.name)}</b>${m.active ? '' : ' <span class="chip c-neutral">desativado</span>'}</td>
        <td>${esc(m.employee || '')}</td>
        <td><span class="status"><span class="dot ${m.online ? 'on' : ''}"></span>${m.online ? 'Conectado' : m.lastSeenMs ? esc(fmtDateTime(m.lastSeenMs)) : 'Nunca conectou'}</span></td>
        <td class="muted">${esc([m.hostname, m.windowsUser].filter(Boolean).join(' · '))}</td>
        <td class="num">${esc(m.agentVersion || '')}</td>
        <td style="white-space:nowrap">
          <button class="small" data-edit="${m.id}">Editar</button>
          <button class="small" data-token="${m.id}">Nova chave</button>
          <button class="small" data-active="${m.id}" data-value="${m.active ? 'false' : 'true'}">${m.active ? 'Desativar' : 'Ativar'}</button>
        </td>
      </tr>`).join('')}</tbody></table></div>`;
  }
  html += `<p class="note">Computador "conectado" enviou dados nos últimos 3 minutos. Desativar faz o computador parar de aparecer nos relatórios e recusa os envios dele.</p></section>`;
  main().innerHTML = html;

  const showToken = (name, token) => {
    document.getElementById('m-token').innerHTML = `
      <div class="callout">
        <b>Chave de ${esc(name)}</b>
        <div class="secret">${esc(token)}</div>
        <span>Esta chave aparece só agora. No computador, abra a pasta do instalador e execute <b>instalar.bat</b> como administrador. Ele vai pedir:</span>
        <span>Endereço do servidor: <span class="secret">${esc(location.origin)}</span></span>
        <span>Chave: a de cima.</span>
      </div>`;
  };

  document.getElementById('m-form').addEventListener('submit', async e => {
    e.preventDefault();
    const name = document.getElementById('m-name').value.trim();
    try {
      const res = await api('POST', '/machines', { name, employee: document.getElementById('m-employee').value });
      await renderMachines();
      showToken(name, res.token);
    } catch (err) { showError(err); }
  });

  main().querySelectorAll('button[data-edit]').forEach(btn => btn.addEventListener('click', async () => {
    const m = machines.find(x => String(x.id) === btn.dataset.edit);
    const name = prompt('Nome do computador', m.name);
    if (name === null) return;
    const employee = prompt('Funcionário', m.employee || '');
    if (employee === null) return;
    try { await api('PUT', `/machines/${m.id}`, { name, employee }); route(); } catch (err) { showError(err); }
  }));

  main().querySelectorAll('button[data-token]').forEach(btn => btn.addEventListener('click', async () => {
    const m = machines.find(x => String(x.id) === btn.dataset.token);
    if (!confirm(`Gerar uma nova chave para ${m.name}? A chave antiga para de funcionar e o programa daquele computador precisa ser reinstalado com a nova.`)) return;
    try { const res = await api('POST', `/machines/${m.id}/token`); showToken(m.name, res.token); } catch (err) { showError(err); }
  }));

  main().querySelectorAll('button[data-active]').forEach(btn => btn.addEventListener('click', async () => {
    try { await api('PUT', `/machines/${btn.dataset.active}`, { active: btn.dataset.value === 'true' }); route(); } catch (err) { showError(err); }
  }));
}

// ---------- Horários ----------

async function renderSchedules() {
  const data = await api('GET', '/settings');
  const current = data.schedules[0];
  const tomorrow = addDays(today(), 1);

  let html = `
    <div class="toolbar"><h1>Horários e configurações</h1></div>
    <section class="card"><h2>Novo horário</h2>
      <p class="note">O horário novo vale a partir da data escolhida. Relatórios de datas anteriores continuam usando o horário que valia na época. Deixe em branco os dias sem expediente.</p>
      <form id="s-form" style="display:flex;flex-direction:column;gap:12px">
        <div class="form-row"><label>Vale a partir de <input id="s-from" type="date" value="${esc(tomorrow)}" required></label></div>
        <div class="sched-grid">
          <span class="muted">Dia</span><span class="muted">Início</span><span class="muted">Fim</span>
          ${DAY_NAMES.map(([key, label]) => {
            const d = current && current.days[key];
            return `<span>${label}</span>
              <input id="s-${key}-start" type="time" value="${esc(d ? d.start : '')}" aria-label="${label} início">
              <input id="s-${key}-end" type="time" value="${esc(d ? d.end : '')}" aria-label="${label} fim">`;
          }).join('')}
        </div>
        <div><button class="primary" type="submit">Salvar horário</button></div>
      </form>
    </section>
    <section class="card"><h2>Histórico de horários</h2>
      <div class="tbl"><table>
        <thead><tr><th>Vale a partir de</th>${DAY_NAMES.map(([, l]) => `<th>${l.slice(0, 3)}</th>`).join('')}<th></th></tr></thead>
        <tbody>${data.schedules.map(s => `<tr>
          <td class="t">${s.effectiveFrom === '2000-01-01' ? 'Início' : esc(fmtDay(s.effectiveFrom))}</td>
          ${DAY_NAMES.map(([key]) => `<td class="t">${s.days[key] ? `${esc(s.days[key].start)}–${esc(s.days[key].end)}` : '<span class="muted">—</span>'}</td>`).join('')}
          <td>${data.schedules.length > 1 ? `<button class="small" data-sdel="${esc(s.effectiveFrom)}">Remover</button>` : ''}</td>
        </tr>`).join('')}</tbody>
      </table></div>
    </section>
    <section class="card"><h2>Inatividade</h2>
      <form id="i-form" class="form-row">
        <label>Minutos sem teclado e mouse para considerar inativo <input id="i-min" type="number" min="1" max="60" value="${data.idleMinutes}" style="width:90px"></label>
        <button class="primary" type="submit">Salvar</button>
      </form>
      <p class="note">Os computadores recebem o novo valor em até 10 minutos. Registros são guardados por ${data.retentionDays} dias.</p>
    </section>
    <section class="card"><h2>Minha senha</h2>
      <form id="p-form" class="form-row">
        <label>Senha atual <input id="p-cur" type="password" autocomplete="current-password" required></label>
        <label>Nova senha <input id="p-new" type="password" autocomplete="new-password" minlength="8" required></label>
        <button class="primary" type="submit">Trocar senha</button>
      </form>
    </section>`;
  main().innerHTML = html;

  document.getElementById('s-form').addEventListener('submit', async e => {
    e.preventDefault();
    const days = {};
    for (const [key] of DAY_NAMES) {
      const start = document.getElementById(`s-${key}-start`).value;
      const end = document.getElementById(`s-${key}-end`).value;
      if (start || end) days[key] = { start, end };
    }
    try {
      await api('POST', '/schedules', { effectiveFrom: document.getElementById('s-from').value, days });
      toast('Horário salvo');
      route();
    } catch (err) { showError(err); }
  });

  main().querySelectorAll('button[data-sdel]').forEach(btn => btn.addEventListener('click', async () => {
    if (!confirm('Remover este horário do histórico? Os relatórios dessas datas passam a usar o horário anterior.')) return;
    try { await api('DELETE', `/schedules/${btn.dataset.sdel}`); route(); } catch (err) { showError(err); }
  }));

  document.getElementById('i-form').addEventListener('submit', async e => {
    e.preventDefault();
    try { await api('PUT', '/settings', { idleMinutes: Number(document.getElementById('i-min').value) }); toast('Salvo'); } catch (err) { showError(err); }
  });

  document.getElementById('p-form').addEventListener('submit', async e => {
    e.preventDefault();
    try {
      await api('POST', '/me/password', { current: document.getElementById('p-cur').value, new: document.getElementById('p-new').value });
      e.target.reset();
      toast('Senha alterada');
    } catch (err) { showError(err); }
  });
}

// ---------- Acessos ----------

async function renderAudit() {
  const rows = await api('GET', '/audit?limit=300');
  main().innerHTML = `
    <div class="toolbar"><h1>Acessos ao painel</h1></div>
    <p class="note">Quem entrou e o que consultou ou alterou. Guardado por 1 ano.</p>
    <div class="tbl"><table>
      <thead><tr><th>Quando</th><th>Usuário</th><th>Ação</th><th>Detalhe</th><th>IP</th></tr></thead>
      <tbody>${rows.map(r => `<tr>
        <td class="t">${esc(fmtDateTime(r.atMs))}</td>
        <td>${esc(r.username || '')}</td>
        <td>${esc(ACTION_NAMES[r.action] || r.action)}</td>
        <td class="title">${esc(r.detail || '')}</td>
        <td class="num muted">${esc(r.ip || '')}</td>
      </tr>`).join('')}</tbody>
    </table></div>`;
}

// ---------- início ----------

(async () => {
  try {
    state.me = await api('GET', '/me');
    showApp();
  } catch {
    showLogin();
  }
})();
