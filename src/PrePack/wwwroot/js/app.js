// Main screen: pick packer → drug → quantity, preview the stickers, print.

const app = {
  today: '',
  config: null,          // safe config from C# (no passwords)
  staff: [],
  workFactors: [],       // [{drugType, maxQty|null, factor}] from MySQL
  dbReady: false,
  drug: null,            // {source:'INVS'|'MANUAL', workingCode, drugName}
  lots: [],              // INVS lots for the chosen drug
  expEdited: false,      // user typed the label EXP themselves
  printing: false,
};

const PREVIEW_MAX_FRAMES = 6;

function typeDef(key) { return (app.config.drugTypes || []).find(t => t.key === key) || null; }

// Mirrors Domain/WorkFactors.Lookup: first band of the type (ascending, open-ended last) with maxQty ≥ qty.
function workFactorFor(type, qty) {
  const bands = app.workFactors.filter(b => b.drugType === type)
    .sort((a, b) => (a.maxQty == null) - (b.maxQty == null) || a.maxQty - b.maxQty);
  const band = bands.find(b => b.maxQty == null || qty <= b.maxQty);
  return band ? band.factor : 1;
}
function layout() { return app.config.layout; }
function perFrame() { return layout().rows * layout().cols; }

// ── Startup ──

async function init() {
  try {
    const d = await call('Init');
    app.today = d.today;
    app.config = d.config;
    app.staff = d.staff || [];
    app.workFactors = d.workFactors || [];
    app.dbReady = d.dbReady;
    $('version').textContent = 'PrePack v' + d.version + ' · ' + d.machine;
    renderStaffSelect();
    renderTypeSelect();
    setDbStatus(d.dbError, d.pendingLogs);
    $('pages').value = 1;
    syncStickersFromPages();
    render();
    if (!d.dbReady) openSettings('db');
  } catch (e) {
    setMsg($('msg'), 'เริ่มโปรแกรมไม่สำเร็จ: ' + e.message, 'err');
  }
}

function setDbStatus(error, pending) {
  const el = $('db-status');
  if (error) { el.textContent = '⚠ ' + error; el.className = 'muted err'; return; }
  el.textContent = pending > 0 ? 'รอส่งบันทึก ' + pending + ' รายการ' : '';
  el.className = 'muted';
}

// Called by settings.js after anything that changes config / staff.
function onConfigChanged(config, staff, workFactors) {
  if (config) app.config = config;
  if (staff) { app.staff = staff; renderStaffSelect(); }
  if (workFactors) app.workFactors = workFactors;
  renderTypeSelect();
  syncStickersFromPages();
  updateExpiry();
  render();
}

function renderStaffSelect() {
  const sel = $('staff');
  const prev = sel.value || String(app.config.lastStaffId || '');
  const active = app.staff.filter(s => s.active);
  sel.innerHTML = '<option value="">— เลือก —</option>' +
    active.map(s => '<option value="' + s.id + '">' + esc(s.name) + '</option>').join('');
  if (active.some(s => String(s.id) === prev)) sel.value = prev;
  sel.classList.toggle('invalid', !sel.value);
}

function renderTypeSelect() {
  const sel = $('drug-type');
  const prev = sel.value;
  sel.innerHTML = '<option value="">— ?? —</option>' +
    app.config.drugTypes.map(t => '<option value="' + t.key + '">' + esc(t.label) + '</option>').join('');
  sel.value = prev;
}

// ── Drug search (INVS) and manual entry ──

let searchSeq = 0;
let activeOpt = -1;
let searchResults = [];

const searchDrugs = debounce(async q => {
  const seq = ++searchSeq;
  const list = $('drug-list');
  if (q.trim().length < 2) { list.hidden = true; return; }
  list.hidden = false;
  list.innerHTML = '<div class="info">กำลังค้นหาใน INVS…</div>';
  try {
    const rows = await call('SearchDrugs', q);
    if (seq !== searchSeq) return;
    searchResults = rows;
    activeOpt = -1;
    list.innerHTML = rows.map((r, i) =>
      '<div class="opt" data-i="' + i + '"><span class="code">' + esc(r.workingCode) + '</span><span>' +
      esc(r.drugName) + '</span><span class="th">' + esc(r.drugNameTh) + '</span></div>').join('') +
      '<div class="opt" data-manual="1"><span class="code">Manual</span><span>ใช้ชื่อ "' + esc(q.trim()) +
      '" (ไม่มีใน INVS)</span></div>';
  } catch (e) {
    if (seq !== searchSeq) return;
    searchResults = [];
    list.innerHTML = '<div class="info">' + esc(e.message) + '</div>' +
      '<div class="opt" data-manual="1"><span class="code">Manual</span><span>ใช้ชื่อ "' + esc(q.trim()) + '"</span></div>';
  }
}, 300);

$('drug').addEventListener('input', () => {
  app.drug = null;
  $('drug-source').textContent = '';
  searchDrugs($('drug').value);
  render();
});

$('drug').addEventListener('keydown', e => {
  const list = $('drug-list');
  const opts = [...list.querySelectorAll('.opt')];
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    if (list.hidden || !opts.length) return;
    e.preventDefault();
    activeOpt = (activeOpt + (e.key === 'ArrowDown' ? 1 : -1) + opts.length) % opts.length;
    opts.forEach((o, i) => o.classList.toggle('active', i === activeOpt));
    opts[activeOpt].scrollIntoView({ block: 'nearest' });
  } else if (e.key === 'Enter') {
    e.preventDefault();
    const pick = opts[activeOpt] || opts.find(o => o.dataset.i === '0') || opts.find(o => o.dataset.manual);
    if (pick) choose(pick); else if ($('drug').value.trim()) chooseManual($('drug').value.trim());
  } else if (e.key === 'Escape') {
    list.hidden = true;
  }
});

$('drug-list').addEventListener('mousedown', e => {
  const opt = e.target.closest('.opt');
  if (opt) { e.preventDefault(); choose(opt); }
});

$('drug').addEventListener('blur', () => {
  setTimeout(() => {
    $('drug-list').hidden = true;
    // Typed a name but never picked: treat as a manual drug so the label still works.
    if (!app.drug && $('drug').value.trim()) chooseManual($('drug').value.trim());
  }, 150);
});

function choose(opt) {
  if (opt.dataset.manual) chooseManual($('drug').value.trim());
  else chooseInvs(searchResults[Number(opt.dataset.i)]);
}

async function chooseInvs(r) {
  $('drug-list').hidden = true;
  setMsg($('msg'), '');
  app.drug = { source: 'INVS', workingCode: r.workingCode, drugName: r.drugName };
  $('drug').value = r.drugName;
  $('drug-source').textContent = 'INVS ' + r.workingCode;
  $('drug-source').className = 'tag';
  setType(r.drugType);
  app.lots = [];
  $('lot').value = '';
  $('src-exp').value = '';
  app.expEdited = false;
  updateExpiry();
  render();
  loadQtyChips();
  try {
    app.lots = await call('GetLots', r.workingCode);
    $('lot-list').innerHTML = app.lots.map(l =>
      '<option value="' + esc(l.lotNo) + '">EXP ' + fmtThaiDate(l.expiryDate) + ' · คงเหลือ ' + fmtNum(l.qty) + '</option>').join('');
    if (app.lots.length) {
      // Default = the lot in stock that expires first.
      $('lot').value = app.lots[0].lotNo;
      $('src-exp').value = app.lots[0].expiryDate || '';
    } else {
      setMsg($('msg'), 'ไม่พบ Lot ที่มีของใน INVS — กรอก Lot / EXP เดิมเอง', 'err');
    }
  } catch (e) {
    setMsg($('msg'), e.message, 'err');
  }
  updateExpiry();
  render();
  $('qty').focus();
}

async function chooseManual(name) {
  $('drug-list').hidden = true;
  if (!name) return;
  app.drug = { source: 'MANUAL', workingCode: '', drugName: name };
  $('drug').value = name;
  $('drug-source').textContent = 'Manual';
  $('drug-source').className = 'tag manual';
  app.lots = [];
  $('lot-list').innerHTML = '';
  app.expEdited = false;
  try { setType(await call('ClassifyName', name)); } catch (e) { setType(''); }
  loadQtyChips();
  updateExpiry();
  render();
}

function setType(key) {
  $('drug-type').value = key || '';
  $('drug-type').classList.toggle('invalid', !key);
  const t = typeDef(key);
  $('qty-unit').textContent = t ? t.unit : '';
}

$('drug-type').addEventListener('change', () => {
  setType($('drug-type').value);
  app.expEdited = false;
  loadQtyChips();
  updateExpiry();
  render();
});

// ── Lot / expiry ──

$('lot').addEventListener('input', () => {
  const lot = app.lots.find(l => l.lotNo.toUpperCase() === $('lot').value.trim().toUpperCase());
  if (lot) $('src-exp').value = lot.expiryDate || '';
  app.expEdited = false;
  updateExpiry();
  render();
});
$('src-exp').addEventListener('input', () => { app.expEdited = false; updateExpiry(); render(); });
$('label-exp').addEventListener('input', () => { app.expEdited = true; updateExpiry(); render(); });

// Label EXP = min(today + shelf days of the type, source EXP); the user may shorten it by hand.
function updateExpiry() {
  const note = $('exp-note');
  const t = typeDef($('drug-type').value);
  const src = $('src-exp').value;
  const r = labelExpiry(app.today, t ? t.shelfDays : 0, src);
  $('src-exp').classList.toggle('invalid', !!(src && src <= app.today));
  if (r.error) {
    if (!app.expEdited) $('label-exp').value = '';
    note.textContent = app.drug ? r.error : '';
    note.className = app.drug && src && src <= app.today ? 'err' : 'muted';
    return;
  }
  if (!app.expEdited) $('label-exp').value = r.date;
  const exp = $('label-exp').value;
  let text = 'Exp = วันนี้ + ' + t.shelfDays + ' วัน (' + t.label + ')' + (r.capped ? ' → ใช้ EXP เดิมเพราะถึงก่อน' : '');
  let bad = false;
  if (app.expEdited) {
    text = 'แก้ EXP ฉลากเอง (ค่าตามสูตร ' + fmtThaiDate(r.date) + ')';
    if (!exp || exp <= app.today) { text = 'EXP ฉลากต้องอยู่หลังวันนี้'; bad = true; }
    else if (src && exp > src) { text = 'EXP ฉลากต้องไม่เกิน EXP เดิม'; bad = true; }
  }
  $('label-exp').classList.toggle('invalid', bad);
  note.textContent = text;
  note.className = bad ? 'err' : 'muted';
}

// ── Quantity shortcuts ──

async function loadQtyChips() {
  const t = typeDef($('drug-type').value);
  let qtys = t ? t.qtyPresets.slice() : [];
  if (app.drug && app.dbReady) {
    try {
      const freq = await call('FrequentQty', app.drug.workingCode || '', app.drug.drugName);
      qtys = freq.concat(qtys.filter(q => !freq.includes(q)));
    } catch (e) { /* shortcuts are optional */ }
  }
  qtys = qtys.slice(0, 8);
  $('qty-chips').innerHTML = qtys.length ? '<span class="muted">จำนวนต่อซอง:</span>' + qtys.map(q =>
    '<button type="button" class="chip" data-q="' + q + '">#' + fmtNum(q) + '</button>').join('') : '';
  markChip();
}

function markChip() {
  const q = Number($('qty').value);
  document.querySelectorAll('#qty-chips .chip').forEach(c => c.classList.toggle('sel', Number(c.dataset.q) === q));
}

$('qty-chips').addEventListener('click', e => {
  const c = e.target.closest('.chip');
  if (!c) return;
  $('qty').value = c.dataset.q;
  markChip();
  render();
});
$('qty').addEventListener('input', () => { markChip(); render(); });

// ── Pages / stickers ──

function clampPages(n) { return Math.max(1, Math.min(200, n || 1)); }

function syncStickersFromPages() {
  const pages = clampPages(parseInt($('pages').value, 10));
  $('pages').value = pages;
  $('stickers').value = pages * perFrame();
}

$('pages').addEventListener('input', () => { syncStickersFromPages(); render(); });
$('pages-minus').addEventListener('click', () => { $('pages').value = clampPages(Number($('pages').value) - 1); syncStickersFromPages(); render(); });
$('pages-plus').addEventListener('click', () => { $('pages').value = clampPages(Number($('pages').value) + 1); syncStickersFromPages(); render(); });
$('stickers').addEventListener('input', () => {
  const n = Math.max(1, Math.min(200 * perFrame(), parseInt($('stickers').value, 10) || 1));
  $('pages').value = Math.ceil(n / perFrame());
  render();
});

function stickerCount() {
  const n = parseInt($('stickers').value, 10);
  return n > 0 ? Math.min(n, 200 * perFrame()) : 1;
}

$('staff').addEventListener('change', () => {
  $('staff').classList.toggle('invalid', !$('staff').value);
  call('SetLastStaff', Number($('staff').value) || 0).catch(() => {});
  render();
});

// ── Preview ──

function currentLabel() {
  const staff = app.staff.find(s => String(s.id) === $('staff').value);
  return {
    drugName: app.drug ? app.drug.drugName : ($('drug').value.trim() || 'ชื่อยา'),
    qty: $('qty').value || '?',
    lotNo: $('lot').value.trim().toUpperCase() || '-',
    packer: staff ? staff.shortName : '-',
    packDate: app.today,
    labelExp: $('label-exp').value,
  };
}

function render() {
  const pv = $('preview');
  if (!app.config) return;
  const count = stickerCount();
  const frames = Math.ceil(count / perFrame());
  if (!app.drug && !$('drug').value.trim()) {
    pv.innerHTML = '<div class="empty">เลือก <b>ผู้บรรจุ</b> → ค้นหา <b>ยา</b> → เลือก <b>#จำนวน</b> แล้วกด <b>พิมพ์</b></div>';
    return;
  }
  pv.innerHTML = buildFrames(currentLabel(), layout(), count, false, PREVIEW_MAX_FRAMES) +
    (frames > PREVIEW_MAX_FRAMES ? '<div class="more">… และอีก ' + (frames - PREVIEW_MAX_FRAMES) + ' หน้า</div>' : '');
}

// ── Print ──

function validateForPrint() {
  const problems = [];
  const mark = (id, bad) => $(id).classList.toggle('invalid', bad);
  mark('staff', !$('staff').value); if (!$('staff').value) problems.push('ผู้บรรจุ');
  mark('drug', !app.drug); if (!app.drug) problems.push('ยา');
  mark('drug-type', !$('drug-type').value); if (!$('drug-type').value) problems.push('ประเภทยา');
  const lot = $('lot').value.trim();
  mark('lot', !lot); if (!lot) problems.push('Lot');
  const qty = Number($('qty').value);
  mark('qty', !(qty > 0)); if (!(qty > 0)) problems.push('#จำนวน');
  if (!$('label-exp').value || $('label-exp').classList.contains('invalid') || $('src-exp').classList.contains('invalid'))
    problems.push('EXP');
  return problems;
}

// Puts the sheets to print into #print-area (the only thing visible under @media print).
function renderPrintArea(label, count, outline) {
  const l = layout();
  $('page-size').textContent = '@page { size: ' + l.frameWidth + 'mm ' + l.frameHeight + 'mm; margin: 0; }';
  $('print-area').innerHTML = buildFrames(label, l, count, outline);
}

// One outlined sheet for checking alignment on the real sticker roll (also used by --print-test-pdf).
function renderTestSheet() {
  renderPrintArea({ drugName: 'ทดสอบตำแหน่ง Paracetamol 500 mg', qty: 30, lotNo: 'TEST01', packer: 'ทดสอบ',
    packDate: app.today, labelExp: addDaysISO(app.today, 365) }, perFrame(), true);
}

async function doPrint(test) {
  if (app.printing) return;
  const msg = $('msg');
  const count = stickerCount();
  const pages = Math.ceil(count / perFrame());
  let label = null;
  if (test) {
    renderTestSheet();
  } else {
    const problems = validateForPrint();
    if (problems.length) { setMsg(msg, 'กรอกให้ครบ: ' + problems.join(', '), 'err'); return; }
    label = currentLabel();
    renderPrintArea(label, count, false);
  }

  app.printing = true;
  $('btn-print').disabled = $('btn-test').disabled = true;
  setMsg(msg, 'กำลังพิมพ์…');
  try {
    const r = await call('Print', JSON.stringify(test ? { test: true, pages: 1, stickers: perFrame() } : {
      staffId: Number($('staff').value),
      source: app.drug.source,
      workingCode: app.drug.workingCode,
      drugName: app.drug.drugName,
      drugType: $('drug-type').value,
      qty: Number($('qty').value),
      lotNo: label.lotNo,
      srcExp: $('src-exp').value || null,
      labelExp: $('label-exp').value,
      packDate: app.today,
      pages, stickers: count,
    }));
    const pts = test ? 0 : count * workFactorFor($('drug-type').value, Number($('qty').value));
    if (test) setMsg(msg, 'พิมพ์ทดสอบแล้ว — ถ้าเลื่อน ให้ปรับที่ ⚙ → สติกเกอร์', 'ok');
    else if (r.queued) setMsg(msg, 'พิมพ์แล้ว ' + count + ' ดวง — บันทึกภาระงานไว้ในเครื่อง จะส่งเข้า MySQL เมื่อเชื่อมต่อได้', 'err');
    else setMsg(msg, 'พิมพ์แล้ว ' + count + ' ดวง (' + pages + ' หน้า) · บันทึกภาระงาน ' + fmtNum(pts) + ' แต้ม', 'ok');
    if (!test) setDbStatus(null, r.pendingLogs || 0);
  } catch (e) {
    setMsg(msg, e.message, 'err');
    if (/วันที่เปลี่ยนแล้ว/.test(e.message)) {
      const d = await call('Init').catch(() => null);
      if (d) { app.today = d.today; updateExpiry(); render(); }
    }
  } finally {
    $('print-area').innerHTML = '';
    app.printing = false;
    $('btn-print').disabled = $('btn-test').disabled = false;
  }
}

$('btn-print').addEventListener('click', () => doPrint(false));
$('btn-test').addEventListener('click', () => doPrint(true));
$('btn-settings').addEventListener('click', () => openSettings());

document.addEventListener('keydown', e => {
  if (e.key === 'F9' || (e.ctrlKey && e.key === 'p')) { e.preventDefault(); if ($('settings').hidden) doPrint(false); }
});

init();
