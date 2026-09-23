// Workload report (per packer) and print history. Source: MySQL (all machines) when reachable,
// otherwise this machine's local database — the banner says which.

const TYPE_COLS = [['tablet', 'เม็ด', 't'], ['cream', 'ครีม', 'c'], ['liquid', 'น้ำ', 'l']];
const reportState = { from: '', to: '', source: '', rows: [], staffUid: '' };

function rangeFilters(prefix) {
  return '<div class="filters">' +
    '<input type="date" id="' + prefix + '-from"> <span class="muted">ถึง</span> <input type="date" id="' + prefix + '-to">' +
    '<button class="small" data-preset="today">วันนี้</button><button class="small" data-preset="week">สัปดาห์นี้</button>' +
    '<button class="small" data-preset="month">เดือนนี้</button>' +
    '<select id="' + prefix + '-source"><option value="">ทุกแหล่ง</option><option value="INVS">INVS</option><option value="MANUAL">Manual</option></select>';
}

function applyPreset(prefix, preset) {
  const t = app.today;
  $(prefix + '-from').value = preset === 'today' ? t : preset === 'week' ? weekStartISO(t) : firstOfMonthISO(t);
  $(prefix + '-to').value = t;
}

function readRange(prefix) {
  return { from: $(prefix + '-from').value, to: $(prefix + '-to').value, source: $(prefix + '-source').value };
}

// ── Workload ──

TAB_LOADERS.report = function () {
  if ($('rp-from')) { loadReport(); return; }
  $('tab-report').innerHTML = rangeFilters('rp') +
    '<button class="right" id="rp-csv">⬇ Export CSV</button></div>' +
    '<div id="rp-origin"></div><div id="rp-kpis" class="kpis"></div>' +
    '<div id="rp-table"></div>' +
    '<div class="legend"><span>แต้มงาน = ดวง × factor (ตั้งที่ ⚙ → แต้มภาระงาน) · % = แต้มของคนนั้น ÷ แต้มรวมทุกคน · ' +
    'คลิกชื่อเพื่อดูรายการที่คนนั้นพิมพ์</span></div>' +
    '<div id="rp-detail"></div>';
  applyPreset('rp', 'month');
  $('tab-report').addEventListener('click', e => {
    const p = e.target.dataset.preset;
    if (p) { applyPreset('rp', p); loadReport(); }
  });
  ['rp-from', 'rp-to', 'rp-source'].forEach(id => $(id).addEventListener('change', loadReport));
  $('rp-csv').addEventListener('click', exportWorkloadCsv);
  $('rp-table').addEventListener('click', e => {
    const tr = e.target.closest('tr[data-staff]');
    if (!tr) return;
    const uid = tr.dataset.staff;
    reportState.staffUid = reportState.staffUid === uid ? '' : uid;
    renderWorkload();
    loadDetail();
  });
  loadReport();
};

async function loadReport() {
  const range = readRange('rp');
  Object.assign(reportState, range, { staffUid: '' });
  $('rp-detail').innerHTML = '';
  try {
    const r = await call('Workload', JSON.stringify(range));
    reportState.rows = r.rows;
    showOrigin('rp-origin', r);
    renderWorkload();
  } catch (e) {
    reportState.rows = [];
    $('rp-kpis').innerHTML = '';
    $('rp-table').innerHTML = '<p class="form-msg err">' + esc(e.message) + '</p>';
  }
}

function renderWorkload() {
  const rows = reportState.rows;
  const sum = k => rows.reduce((a, r) => a + r[k], 0);
  const items = sum('items');
  const totalPoints = sum('points');
  $('rp-kpis').innerHTML = [
    ['รายการที่พิมพ์', fmtNum(items)], ['ดวง (ซอง)', fmtNum(sum('stickers'))], ['แต้มงานรวม', fmtNum(totalPoints)],
    ['ยา Manual', items ? Math.round(sum('manual') / items * 100) + '%' : '-'],
  ].map(([k, v]) => '<div class="kpi"><div class="k">' + k + '</div><div class="v">' + v + '</div></div>').join('');

  if (!rows.length) { $('rp-table').innerHTML = '<p class="muted">ไม่มีการพิมพ์ในช่วงนี้</p>'; return; }
  const max = Math.max(...rows.map(r => r.points), 0.0001);
  $('rp-table').innerHTML = '<table><thead><tr><th>ผู้บรรจุ</th><th class="num">รายการ</th><th class="num">หน้า</th>' +
    TYPE_COLS.map(([, t]) => '<th class="num">ดวง' + t + '</th>').join('') +
    '<th class="num">รวมดวง</th><th class="num">แต้มงาน</th><th class="num">% ภาระงาน</th><th style="width:22%"></th></tr></thead><tbody>' +
    rows.map(r => '<tr class="clickable' + (r.staffUid === reportState.staffUid ? ' selected' : '') + '" data-staff="' + esc(r.staffUid) + '">' +
      '<td>' + esc(r.name) + '</td><td class="num">' + fmtNum(r.items) + '</td><td class="num">' + fmtNum(r.pages) + '</td>' +
      '<td class="num">' + fmtNum(r.tablet) + '</td><td class="num">' + fmtNum(r.cream) + '</td><td class="num">' + fmtNum(r.liquid) + '</td>' +
      '<td class="num">' + fmtNum(r.stickers) + '</td><td class="num">' + fmtNum(r.points) + '</td>' +
      '<td class="num"><b>' + pct(r.points, totalPoints) + '</b></td>' +
      '<td><div class="wbar" style="width:' + (r.points / max * 100) + '%"></div></td></tr>').join('') +
    '</tbody></table>';
}

// Banner: which database the numbers came from. Local = this machine only, so % can mislead.
function showOrigin(id, r) {
  if (r.sync) setSyncStatus(r.sync);
  $(id).innerHTML = r.origin === 'mysql'
    ? '<div class="banner ok">ข้อมูลจาก MySQL — รวมทุกเครื่อง</div>'
    : '<div class="banner warn"><b>ข้อมูลจากเครื่องนี้เท่านั้น</b> (' + esc(r.reason || '') + ')' +
      (r.sync && r.sync.unsynced ? ' · ยังไม่ได้ sync ' + r.sync.unsynced + ' รายการ' : '') +
      ' — % ภาระงานยังไม่รวมเครื่องอื่น</div>';
}

function pct(part, total) { return total > 0 ? (Math.round(part / total * 1000) / 10).toFixed(1) + '%' : '-'; }

async function loadDetail() {
  const box = $('rp-detail');
  if (!reportState.staffUid) { box.innerHTML = ''; return; }
  const row = reportState.rows.find(r => r.staffUid === reportState.staffUid);
  box.innerHTML = '<p class="muted">กำลังโหลด…</p>';
  try {
    const logs = (await call('Logs', JSON.stringify({ from: reportState.from, to: reportState.to,
      source: reportState.source, staffUid: reportState.staffUid, lot: '' }))).rows;
    box.innerHTML = '<div class="card" style="margin-top:14px"><h3>รายการของ ' + esc(row ? row.name : '') + '</h3>' + logsTable(logs) + '</div>';
  } catch (e) { box.innerHTML = '<p class="form-msg err">' + esc(e.message) + '</p>'; }
}

async function exportWorkloadCsv() {
  const s = reportState;
  const total = s.rows.reduce((a, r) => a + r.points, 0);
  const lines = [csvLine(['ผู้บรรจุ', 'รายการ', 'หน้า', 'ดวงยาเม็ด', 'ดวงครีม', 'ดวงยาน้ำ', 'รวมดวง', 'แต้มงาน', '% ภาระงาน', 'รายการ Manual'])];
  s.rows.forEach(r => lines.push(csvLine([r.name, r.items, r.pages, r.tablet, r.cream, r.liquid, r.stickers, r.points,
    pct(r.points, total), r.manual])));
  try {
    await call('SaveCsv', 'prepack-workload-' + s.from + '_' + s.to + '.csv', lines.join('\r\n'));
  } catch (e) { alert(e.message); }
}

// ── History / recall search ──

function typeLabel(key) { const t = typeDef(key); return t ? t.label : key; }

function logsTable(logs) {
  if (!logs.length) return '<p class="muted">ไม่มีรายการ</p>';
  return '<table><thead><tr><th>เวลาพิมพ์</th><th>ผู้บรรจุ</th><th>ยา</th><th>ประเภท</th><th class="num">#</th><th>Lot</th>' +
    '<th>EXP ฉลาก</th><th class="num">หน้า</th><th class="num">ดวง</th><th class="num">factor</th><th class="num">แต้ม</th>' +
    '<th>เครื่อง</th></tr></thead><tbody>' +
    logs.map(l => '<tr><td>' + fmtThaiDate(l.printedAt.slice(0, 10)) + ' ' + l.printedAt.slice(11, 16) +
      (l.synced ? '' : ' <span class="tag manual" title="ยังไม่ได้ส่งขึ้น MySQL">รอ sync</span>') + '</td>' +
      '<td>' + esc(l.staffName) + '</td><td>' + esc(l.drugName) +
      (l.source === 'MANUAL' ? ' <span class="tag manual">Manual</span>' : ' <span class="muted">' + esc(l.workingCode || '') + '</span>') + '</td>' +
      '<td>' + esc(typeLabel(l.drugType)) + '</td><td class="num">' + fmtNum(l.qtyPerPack) + ' ' + esc(l.unit) + '</td>' +
      '<td>' + esc(l.lotNo) + '</td><td>' + fmtThaiDate(l.labelExpDate) + '</td>' +
      '<td class="num">' + l.pages + '</td><td class="num">' + l.stickers + '</td><td class="num">' + fmtNum(l.workFactor) + '</td>' +
      '<td class="num">' + fmtNum(l.workPoints) + '</td><td class="muted">' + esc(l.machineName) + '</td></tr>').join('') +
    '</tbody></table>' + (logs.length >= 500 ? '<p class="note">แสดง 500 รายการล่าสุด — ย่อช่วงวันที่เพื่อดูเพิ่ม</p>' : '');
}

let historyLogs = [];

TAB_LOADERS.history = function () {
  if ($('hi-from')) { loadHistory(); return; }
  $('tab-history').innerHTML = rangeFilters('hi') +
    '<input id="hi-lot" placeholder="ค้นหา Lot (recall)" style="width:180px">' +
    '<button class="right" id="hi-csv">⬇ Export CSV</button></div><div id="hi-origin"></div><div id="hi-table"></div>';
  applyPreset('hi', 'week');
  $('tab-history').addEventListener('click', e => {
    const p = e.target.dataset.preset;
    if (p) { applyPreset('hi', p); loadHistory(); }
  });
  ['hi-from', 'hi-to', 'hi-source'].forEach(id => $(id).addEventListener('change', loadHistory));
  $('hi-lot').addEventListener('input', debounce(loadHistory, 400));
  $('hi-csv').addEventListener('click', async () => {
    const lines = [csvLine(['เวลาพิมพ์', 'ผู้บรรจุ', 'แหล่ง', 'รหัสยา', 'ชื่อยา', 'ประเภท', 'จำนวนต่อซอง', 'หน่วย', 'Lot',
      'วันบรรจุ', 'EXP เดิม', 'EXP ฉลาก', 'หน้า', 'ดวง', 'factor', 'แต้มงาน', 'เครื่อง'])];
    historyLogs.forEach(l => lines.push(csvLine([l.printedAt.replace('T', ' '), l.staffName, l.source, l.workingCode, l.drugName,
      typeLabel(l.drugType), l.qtyPerPack, l.unit, l.lotNo, l.packDate, l.srcExpDate, l.labelExpDate, l.pages, l.stickers,
      l.workFactor, l.workPoints, l.machineName])));
    try { await call('SaveCsv', 'prepack-history-' + $('hi-from').value + '_' + $('hi-to').value + '.csv', lines.join('\r\n')); }
    catch (e) { alert(e.message); }
  });
  loadHistory();
};

async function loadHistory() {
  try {
    const r = await call('Logs', JSON.stringify(Object.assign(readRange('hi'), { staffUid: '', lot: $('hi-lot').value })));
    historyLogs = r.rows;
    showOrigin('hi-origin', r);
    $('hi-table').innerHTML = logsTable(historyLogs);
  } catch (e) {
    historyLogs = [];
    $('hi-table').innerHTML = '<p class="form-msg err">' + esc(e.message) + '</p>';
  }
}
