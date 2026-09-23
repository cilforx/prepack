// ⚙ panel: staff, sticker layout + printer, drug types, database connections.
// Report and history tabs live in report.js.

const TAB_LOADERS = {};

function openSettings(tab) {
  $('settings').hidden = false;
  showTab(tab || document.querySelector('#settings-tabs button.active').dataset.tab);
}

function showTab(tab) {
  document.querySelectorAll('#settings-tabs button[data-tab]').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
  document.querySelectorAll('#settings .tab').forEach(t => t.classList.toggle('active', t.id === 'tab-' + tab));
  if (TAB_LOADERS[tab]) TAB_LOADERS[tab]();
}

$('settings-tabs').addEventListener('click', e => {
  const b = e.target.closest('button[data-tab]');
  if (b) showTab(b.dataset.tab);
});
$('btn-close-settings').addEventListener('click', () => { $('settings').hidden = true; });
document.addEventListener('keydown', e => { if (e.key === 'Escape' && !$('settings').hidden) { $('settings').hidden = true; factorPassword = null; } });

function formMsg(id, text, kind) { const el = $(id); el.textContent = text || ''; el.className = 'form-msg ' + (kind || ''); }

// ── Staff ──

TAB_LOADERS.staff = function () {
  $('tab-staff').innerHTML =
    '<div class="card"><h3 id="st-title">เพิ่มเจ้าหน้าที่</h3>' +
    '<input type="hidden" id="st-id"><div class="grid">' +
    '<label class="fld">ชื่อ-สกุล<input id="st-name" maxlength="100"></label>' +
    '<label class="fld">ชื่อบนฉลาก (สั้น)<input id="st-short" maxlength="30" placeholder="เว้นว่าง = ใช้ชื่อเต็ม"></label>' +
    '<label class="check" id="st-active-wrap" hidden><input type="checkbox" id="st-active"> ใช้งาน</label>' +
    '</div><div class="row" style="margin-top:12px"><button class="primary" id="st-save">บันทึก</button>' +
    '<button id="st-reset">ล้างฟอร์ม</button></div><div id="st-msg" class="form-msg"></div></div>' +
    '<div class="card"><h3>รายชื่อ</h3><div id="st-list"></div></div>';

  const reset = () => {
    $('st-id').value = ''; $('st-name').value = ''; $('st-short').value = '';
    $('st-active-wrap').hidden = true; $('st-title').textContent = 'เพิ่มเจ้าหน้าที่';
  };
  const list = () => {
    if (!app.staff.length) { $('st-list').innerHTML = '<p class="muted">ยังไม่มีเจ้าหน้าที่</p>'; return; }
    $('st-list').innerHTML = '<table><thead><tr><th>ชื่อ</th><th>บนฉลาก</th><th>สถานะ</th><th></th></tr></thead><tbody>' +
      app.staff.map(s => '<tr><td>' + esc(s.name) + '</td><td>' + esc(s.shortName) + '</td><td>' +
        (s.active ? 'ใช้งาน' : '<span class="muted">ปิด</span>') + '</td><td><button class="small" data-edit="' + esc(s.uid) +
        '">แก้ไข</button></td></tr>').join('') + '</tbody></table>';
  };
  list();

  $('st-list').addEventListener('click', e => {
    const uid = e.target.dataset.edit;
    if (!uid) return;
    const s = app.staff.find(x => x.uid === uid);
    $('st-id').value = s.uid; $('st-name').value = s.name; $('st-short').value = s.shortName;
    $('st-active').checked = s.active; $('st-active-wrap').hidden = false; $('st-title').textContent = 'แก้ไขเจ้าหน้าที่';
    $('st-name').focus();
  });
  $('st-reset').addEventListener('click', () => { reset(); formMsg('st-msg', ''); });
  $('st-save').addEventListener('click', async () => {
    try {
      const r = await call('SaveStaff', JSON.stringify({
        uid: $('st-id').value, name: $('st-name').value, shortName: $('st-short').value,
        active: $('st-id').value ? $('st-active').checked : true,
      }));
      onConfigChanged(null, r.staff);
      setSyncStatus(r.sync);
      reset(); list();
      formMsg('st-msg', r.online ? 'บันทึกลง server แล้ว' : 'บันทึกในเครื่องแล้ว (จะส่งขึ้น server เมื่อเชื่อมได้)', 'ok');
    } catch (e) { formMsg('st-msg', e.message, 'err'); }
  });
};

// ── Sticker layout + printer ──

const LAYOUT_FIELDS = [
  ['frameWidth', 'กว้างแผ่น (มม.)', 0.1], ['frameHeight', 'สูงแผ่น (มม.)', 0.1],
  ['headerHeight', 'ส่วนหัว ฉีกทิ้ง (มม.)', 0.1], ['footerHeight', 'ส่วนท้าย ฉีกทิ้ง (มม.)', 0.1],
  ['rows', 'จำนวนแถว', 1], ['cols', 'จำนวนคอลัมน์', 1], ['fontSize', 'ขนาดอักษร (pt)', 0.1],
  ['gapX', 'ช่องว่างแนวนอน (มม.)', 0.1], ['gapY', 'ช่องว่างแนวตั้ง (มม.)', 0.1], ['padding', 'ขอบในดวง (มม.)', 0.1],
  ['offsetX', 'เลื่อนขวา + / ซ้าย − (มม.)', 0.1], ['offsetY', 'เลื่อนลง + / ขึ้น − (มม.)', 0.1],
];

// "13, 13, 14" → [13, 13, 14]; blank → [] (= split evenly). Accepts spaces or commas.
function parseSizes(text) {
  return String(text || '').split(/[,\s]+/).filter(Boolean).map(Number);
}
function sizesText(list) { return (list || []).map(fmtNum).join(', '); }

TAB_LOADERS.label = async function () {
  const l = app.config.layout;
  $('tab-label').innerHTML =
    '<div class="card"><h3>เครื่องพิมพ์</h3><div class="row"><select id="lb-printer" style="max-width:420px"></select>' +
    '<button id="lb-refresh" class="small">โหลดรายชื่อใหม่</button></div>' +
    '<p class="note">พิมพ์แบบ silent ไปที่เครื่องนี้โดยตรง ขนาดกระดาษ = ขนาดแผ่นด้านล่าง ไม่มีขอบ ไม่มี header/footer</p></div>' +
    '<div class="card"><h3>ขนาดสติกเกอร์</h3><div class="grid">' +
    LAYOUT_FIELDS.map(([k, t, step]) => '<label class="fld">' + t + '<input type="number" step="' + step + '" id="lb-' + k + '" value="' + (l[k] || 0) + '"></label>').join('') +
    '<label class="check"><input type="checkbox" id="lb-buddhistYear"' + (l.buddhistYear ? ' checked' : '') + '> ปี พ.ศ.</label>' +
    '</div>' +
    '<h3 style="margin-top:20px">ขนาดแต่ละแถว / คอลัมน์ <span class="muted">(เว้นว่าง = แบ่งเท่ากัน · คั่นด้วย , เช่น 13, 13, 14)</span></h3>' +
    '<div class="grid" style="grid-template-columns:1fr 1fr">' +
    '<label class="fld">ความสูงแต่ละแถว บน→ล่าง (มม.)<input id="lb-rowHeights" value="' + esc(sizesText(l.rowHeights)) + '"></label>' +
    '<label class="fld">ความกว้างแต่ละคอลัมน์ ซ้าย→ขวา (มม.)<input id="lb-colWidths" value="' + esc(sizesText(l.colWidths)) + '"></label>' +
    '<div class="note" id="lb-rowinfo" style="margin:0"></div><div class="note" id="lb-colinfo" style="margin:0"></div>' +
    '<div><button type="button" class="small" id="lb-row-even">ใส่ค่าแบ่งเท่ากัน</button></div>' +
    '<div><button type="button" class="small" id="lb-col-even">ใส่ค่าแบ่งเท่ากัน</button></div>' +
    '</div>' +
    '<div class="row" style="margin-top:16px"><button class="primary" id="lb-save">บันทึก</button>' +
    '<button id="lb-default">ค่าเริ่มต้น 85×50 มม. 3×3</button></div><div id="lb-msg" class="form-msg"></div></div>' +
    '<div class="card"><h3>ตัวอย่าง <span class="muted" id="lb-cell"></span></h3><div id="lb-preview" class="label-preview"></div>' +
    '<p class="note">กด "ทดสอบตำแหน่ง" ที่หน้าหลักเพื่อพิมพ์เส้นขอบ ถ้าเลื่อน ให้แก้ค่า "เลื่อนขวา / เลื่อนลง"</p></div>';

  const read = () => {
    const o = {};
    LAYOUT_FIELDS.forEach(([k]) => { o[k] = Number($('lb-' + k).value); });
    o.buddhistYear = $('lb-buddhistYear').checked;
    o.rowHeights = parseSizes($('lb-rowHeights').value);
    o.colWidths = parseSizes($('lb-colWidths').value);
    return o;
  };
  // How much room the rows/columns have, so the user knows what numbers fit.
  const info = o => {
    const rs = rowSpace(o), cs = colSpace(o);
    const used = (list, n) => list.length === n ? ' · ใส่แล้วรวม ' + fmtNum(sumOf(list)) + ' มม.' : list.length ? ' · ใส่ ' + list.length + '/' + n + ' ค่า' : '';
    $('lb-rowinfo').textContent = 'ที่ว่าง ' + fmtNum(rs) + ' มม. (แบ่งเท่ากัน = แถวละ ' + fmtNum(rs / o.rows) + ')' + used(o.rowHeights, o.rows);
    $('lb-colinfo').textContent = 'ที่ว่าง ' + fmtNum(cs) + ' มม. (แบ่งเท่ากัน = คอลัมน์ละ ' + fmtNum(cs / o.cols) + ')' + used(o.colWidths, o.cols);
  };
  const preview = () => {
    const o = read();
    info(o);
    const err = validateLayout(o);
    if (err) { $('lb-preview').innerHTML = '<p class="form-msg err">' + esc(err) + '</p>'; $('lb-cell').textContent = ''; return; }
    const sample = { drugName: 'Amoxicillin/Clavulanate 1 g (Augmentin)', qty: 100, lotNo: 'A12345',
      packer: 'สมชาย ใจดีมากมาก', packDate: app.today, labelExp: addDaysISO(app.today, 365) };
    $('lb-preview').innerHTML = buildFrames(sample, o, o.rows * o.cols, true);
    const hs = rowHeightsOf(o), ws = colWidthsOf(o);
    $('lb-cell').textContent = '· แถวสูง ' + hs.map(fmtNum).join(' / ') + ' · คอลัมน์กว้าง ' + ws.map(fmtNum).join(' / ') + ' มม.';
  };
  // Even split rounded to 0.1 mm; the last one takes the remainder so the total still fits exactly.
  const evenList = (space, n) => {
    const each = Math.floor(space / n * 10) / 10;
    return Array.from({ length: n }, (_, i) => i < n - 1 ? each : Math.round((space - each * (n - 1)) * 10) / 10);
  };
  $('lb-row-even').onclick = () => { const o = read(); $('lb-rowHeights').value = sizesText(evenList(rowSpace(o), o.rows)); preview(); };
  $('lb-col-even').onclick = () => { const o = read(); $('lb-colWidths').value = sizesText(evenList(colSpace(o), o.cols)); preview(); };
  const loadPrinters = async () => {
    try {
      const printers = await call('GetPrinters');
      const cur = app.config.printerName;
      const names = printers.includes(cur) || !cur ? printers : [cur].concat(printers);
      $('lb-printer').innerHTML = '<option value="">— เลือกเครื่องพิมพ์ —</option>' +
        names.map(p => '<option' + (p === cur ? ' selected' : '') + '>' + esc(p) + '</option>').join('');
    } catch (e) { formMsg('lb-msg', e.message, 'err'); }
  };

  $('tab-label').oninput = preview; // the tab container is reused, so assign rather than add listeners
  $('lb-default').addEventListener('click', () => {
    const d = { frameWidth: 85, frameHeight: 50, headerHeight: 8, footerHeight: 0, rows: 3, cols: 3, gapX: 1, gapY: 1,
      offsetX: 0, offsetY: 0, padding: 0.8, fontSize: 5.5 };
    Object.keys(d).forEach(k => { $('lb-' + k).value = d[k]; });
    $('lb-rowHeights').value = ''; $('lb-colWidths').value = '';
    preview();
  });
  $('lb-refresh').addEventListener('click', loadPrinters);
  $('lb-save').addEventListener('click', async () => {
    const o = read();
    const err = validateLayout(o);
    if (err) { formMsg('lb-msg', err, 'err'); return; }
    try {
      onConfigChanged(await call('SaveLabelSettings', JSON.stringify({ layout: o, printerName: $('lb-printer').value })));
      formMsg('lb-msg', 'บันทึกแล้ว', 'ok');
    } catch (e) { formMsg('lb-msg', e.message, 'err'); }
  });
  preview();
  await loadPrinters();
};

// ── Screen zoom (this machine only, like BoxBox "ขนาดแสดงผล"; does not change printed stickers) ──

const ZOOM_STEPS = [50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100, 110, 125, 150];

TAB_LOADERS.display = async function () {
  let current = app.config.uiZoomPercent || 100;
  try { current = await call('GetUiZoom'); } catch (e) { /* use the value from start-up */ }
  const render = () => {
    $('tab-display').innerHTML =
      '<div class="card"><h3>🔍 ขนาดแสดงผล <span class="muted">(เฉพาะเครื่องนี้)</span></h3>' +
      '<p class="note" style="margin-top:0">ปรับถ้าหน้าจอใหญ่หรือเล็กเกินไป — จำค่าไว้ในเครื่องนี้ ไม่ sync ข้ามเครื่อง ' +
      'และไม่มีผลกับขนาดสติกเกอร์ที่พิมพ์ · ใช้ Ctrl + ลูกกลิ้งเมาส์ก็ได้ (จำค่าเหมือนกัน)</p>' +
      '<div class="chips" style="margin-top:12px">' +
      ZOOM_STEPS.map(v => '<button type="button" class="chip' + (v === current ? ' sel' : '') + '" data-zoom="' + v + '">' + v + '%</button>').join('') +
      '</div><div id="zm-msg" class="form-msg"></div></div>';
  };
  render();
  $('tab-display').onclick = async e => {
    const v = Number(e.target.dataset.zoom);
    if (!v) return;
    try {
      current = await call('SetUiZoom', v);
      app.config.uiZoomPercent = current;
      render();
      formMsg('zm-msg', 'ตั้งเป็น ' + current + '% แล้ว', 'ok');
    } catch (err) { formMsg('zm-msg', err.message, 'err'); }
  };
};

// ── Drug types, shelf life, unit table, name keywords ──

TAB_LOADERS.types = function () {
  const c = app.config;
  const typeOpts = key => c.drugTypes.map(t => '<option value="' + t.key + '"' + (t.key === key ? ' selected' : '') + '>' + esc(t.label) + '</option>').join('');
  const unitRows = Object.entries(c.unitMap).map(([u, k]) =>
    '<tr><td><input data-unit value="' + esc(u) + '"></td><td><select data-unit-type>' + typeOpts(k) + '</select></td>' +
    '<td><button class="small danger" data-del>ลบ</button></td></tr>').join('');

  $('tab-types').innerHTML =
    '<div class="card"><h3>อายุยาบนฉลาก (Exp = วันบรรจุ + จำนวนวัน แต่ไม่เกิน EXP เดิม)</h3>' +
    '<table><thead><tr><th>ประเภท</th><th>หน่วยในระบบ</th><th>อายุ (วัน)</th><th>ปุ่มลัด #จำนวน (คั่นด้วย ,)</th></tr></thead><tbody>' +
    c.drugTypes.map(t => '<tr data-type="' + t.key + '"><td>' + esc(t.label) + '</td>' +
      '<td><input data-f="unit" value="' + esc(t.unit) + '" style="width:90px"></td>' +
      '<td><input data-f="shelfDays" type="number" min="1" max="3650" value="' + t.shelfDays + '" style="width:100px"></td>' +
      '<td><input data-f="qtyPresets" value="' + esc(t.qtyPresets.join(', ')) + '"></td></tr>').join('') +
    '</tbody></table></div>' +
    '<div class="card"><h3>คำในชื่อยา → ประเภท <span class="muted">(ชื่อยาชนะหน่วย · เทียบทั้งคำ ไม่สนตัวพิมพ์เล็ก/ใหญ่)</span></h3>' +
    c.drugTypes.map(t => '<label class="fld" style="margin-bottom:8px">' + esc(t.label) +
      '<input data-kw="' + t.key + '" value="' + esc((c.nameKeywords[t.key] || []).join(', ')) + '"></label>').join('') +
    '</div>' +
    '<div class="card"><h3>หน่วยจาก INVS → ประเภท <span class="muted">(ใช้เมื่อชื่อยาไม่บอก)</span></h3>' +
    '<table><thead><tr><th>หน่วย</th><th>ประเภท</th><th></th></tr></thead><tbody id="ty-units">' + unitRows + '</tbody></table>' +
    '<button class="small" id="ty-add" style="margin-top:8px">+ เพิ่มหน่วย</button></div>' +
    '<div class="row"><button class="primary" id="ty-save">บันทึก</button><button id="ty-reset">คืนค่าเริ่มต้น</button></div>' +
    '<div id="ty-msg" class="form-msg"></div>';

  $('ty-add').addEventListener('click', () => {
    $('ty-units').insertAdjacentHTML('beforeend', '<tr><td><input data-unit></td><td><select data-unit-type>' +
      typeOpts('tablet') + '</select></td><td><button class="small danger" data-del>ลบ</button></td></tr>');
  });
  $('ty-units').addEventListener('click', e => { if (e.target.dataset.del !== undefined) e.target.closest('tr').remove(); });
  $('ty-save').addEventListener('click', async () => {
    const split = s => s.split(',').map(x => x.trim()).filter(Boolean);
    const drugTypes = c.drugTypes.map(t => {
      const row = document.querySelector('#tab-types tr[data-type="' + t.key + '"]');
      const v = f => row.querySelector('[data-f="' + f + '"]').value;
      return { key: t.key, label: t.label, unit: v('unit'), shelfDays: Number(v('shelfDays')),
        qtyPresets: split(v('qtyPresets')).map(Number).filter(n => n > 0) };
    });
    const nameKeywords = {};
    document.querySelectorAll('#tab-types [data-kw]').forEach(i => { nameKeywords[i.dataset.kw] = split(i.value); });
    const unitMap = {};
    document.querySelectorAll('#ty-units tr').forEach(tr => {
      const u = tr.querySelector('[data-unit]').value.trim();
      if (u) unitMap[u] = tr.querySelector('[data-unit-type]').value;
    });
    try {
      onConfigChanged(await call('SaveDrugTypes', JSON.stringify({ drugTypes, nameKeywords, unitMap })));
      formMsg('ty-msg', 'บันทึกแล้ว', 'ok');
    } catch (e) { formMsg('ty-msg', e.message, 'err'); }
  });
  $('ty-reset').addEventListener('click', async () => {
    if (!confirm('คืนค่าอายุยา ตารางหน่วย และคำในชื่อยา เป็นค่าเริ่มต้น?')) return;
    try { onConfigChanged(await call('ResetDrugTypes')); TAB_LOADERS.types(); formMsg('ty-msg', 'คืนค่าแล้ว', 'ok'); }
    catch (e) { formMsg('ty-msg', e.message, 'err'); }
  });
};

// ── Workload factors (password protected; stored in MySQL so every machine scores the same) ──

let factorPassword = null; // set after a correct password; cleared when the panel closes

TAB_LOADERS.factors = async function () {
  const box = $('tab-factors');
  box.innerHTML = '<p class="muted">กำลังโหลด…</p>';
  let table = app.workFactors;
  try { table = await call('GetWorkFactors'); onConfigChanged(null, null, table); }
  catch (e) { if (!table.length) { box.innerHTML = '<p class="form-msg err">' + esc(e.message) + '</p>'; return; } }
  renderFactors(table);
};

function renderFactors(table) {
  const locked = factorPassword == null;
  const dis = locked ? ' disabled' : '';
  const byType = app.config.drugTypes.map(t => {
    const bands = table.filter(b => b.drugType === t.key)
      .sort((a, b) => (a.maxQty == null) - (b.maxQty == null) || a.maxQty - b.maxQty);
    let from = 1;
    const rows = bands.map(b => {
      const range = b.maxQty == null ? '#' + fmtNum(from) + ' ขึ้นไป' : '#' + fmtNum(from) + ' – ' + fmtNum(b.maxQty);
      if (b.maxQty != null) from = Number(b.maxQty) + 1;
      return '<tr data-type="' + t.key + '"><td>' + range + '</td>' +
        '<td>' + (b.maxQty == null ? '<input data-max value="" disabled placeholder="ขึ้นไป" style="width:110px">'
          : '<input data-max type="number" min="1" step="any" value="' + b.maxQty + '" style="width:110px"' + dis + '>') + '</td>' +
        '<td><input data-factor type="number" min="0" max="100" step="0.1" value="' + b.factor + '" style="width:100px"' + dis + '></td>' +
        '<td>' + (locked || b.maxQty == null ? '' : '<button class="small danger" data-del>ลบ</button>') + '</td></tr>';
    }).join('');
    return '<div class="card"><h3>' + esc(t.label) + '</h3><table><thead><tr><th>ช่วง #จำนวนต่อซอง</th>' +
      '<th>ถึง # (สูงสุดของช่วง)</th><th>factor ต่อดวง</th><th></th></tr></thead><tbody>' + rows + '</tbody></table>' +
      (locked ? '' : '<button class="small" data-add="' + t.key + '" style="margin-top:8px">+ เพิ่มช่วง</button>') + '</div>';
  }).join('');

  $('tab-factors').innerHTML =
    '<div class="card"><h3>แต้มภาระงาน = จำนวนดวง × factor</h3>' +
    '<p class="note" style="margin-top:0">factor ขึ้นกับประเภทยาและ #จำนวนต่อซอง เช่น ซอง #100 นับยากกว่า #30 ก็ให้ factor สูงกว่า · ' +
    'ค่าเริ่มต้น = 1 ทุกช่วง (ทุกดวงนับเท่ากัน) · ใช้ร่วมกันทุกเครื่อง (เก็บใน MySQL) · ' +
    'แก้แล้วมีผลกับการพิมพ์ครั้งต่อไปเท่านั้น ประวัติเดิมไม่เปลี่ยน</p>' +
    (locked
      ? '<div class="row"><input type="password" id="wf-pass" placeholder="รหัสสำหรับแก้ไข" style="width:200px" ' +
        'autocomplete="off" lang="en">' +
        '<button type="button" class="icon-btn" id="wf-show" title="แสดง/ซ่อนรหัส" aria-label="แสดงรหัส">👁</button>' +
        '<button id="wf-unlock">🔒 ปลดล็อกเพื่อแก้ไข</button></div>' +
        '<div id="wf-hint" class="note"></div>'
      : '<div class="row"><span class="tag">ปลดล็อกแล้ว</span><button class="primary" id="wf-save">บันทึก</button>' +
        '<button id="wf-lock">ล็อก</button></div>') +
    '<div id="wf-msg" class="form-msg"></div></div>' + byType;

  const box = $('tab-factors');
  box.onclick = async e => {
    const t = e.target;
    if (t.id === 'wf-unlock') {
      const pw = $('wf-pass').value.trim();
      try { await call('CheckAdminPassword', pw); factorPassword = pw; renderFactors(readFactors(table)); }
      catch (err) { formMsg('wf-msg', err.message + passwordHint(pw), 'err'); }
    } else if (t.id === 'wf-show') {
      const inp = $('wf-pass');
      inp.type = inp.type === 'password' ? 'text' : 'password';
      inp.focus();
    } else if (t.id === 'wf-lock') {
      factorPassword = null; renderFactors(app.workFactors);
    } else if (t.dataset.add) {
      const cur = readFactors(table);
      const maxes = cur.filter(b => b.drugType === t.dataset.add && b.maxQty != null).map(b => b.maxQty);
      cur.push({ drugType: t.dataset.add, maxQty: (maxes.length ? Math.max(...maxes) : 0) + 10, factor: 1 });
      renderFactors(cur);
    } else if (t.dataset.del !== undefined) {
      t.closest('tr').remove();
      renderFactors(readFactors(table));
    } else if (t.id === 'wf-save') {
      try {
        const saved = await call('SaveWorkFactors', JSON.stringify(readFactors(table)), factorPassword);
        onConfigChanged(null, null, saved);
        renderFactors(saved);
        formMsg('wf-msg', 'บันทึกแล้ว', 'ok');
      } catch (err) { formMsg('wf-msg', err.message, 'err'); }
    }
  };
  if ($('wf-pass')) {
    const hint = e => {
      const caps = e && e.getModifierState && e.getModifierState('CapsLock');
      $('wf-hint').textContent = (passwordHint($('wf-pass').value) + (caps ? ' · Caps Lock เปิดอยู่' : '')).replace(/^ · /, '');
    };
    $('wf-pass').onkeydown = e => { if (e.key === 'Enter') $('wf-unlock').click(); };
    $('wf-pass').onkeyup = hint;
    $('wf-pass').oninput = () => hint();
  }
}

// Password fields hide what was typed, so warn about the usual reason a correct password fails.
function passwordHint(pw) {
  return /[฀-๿]/.test(pw) ? ' · แป้นพิมพ์เป็นภาษาไทยอยู่ — กด ~ เปลี่ยนเป็นภาษาอังกฤษ' : '';
}

// Current rows on screen → [{drugType, maxQty, factor}]; falls back to `table` before the first render.
function readFactors(table) {
  const rows = [...document.querySelectorAll('#tab-factors tr[data-type]')];
  if (!rows.length) return table.slice();
  return rows.map(tr => {
    const max = tr.querySelector('[data-max]').value;
    return { drugType: tr.dataset.type, maxQty: max === '' ? null : Number(max), factor: Number(tr.querySelector('[data-factor]').value) };
  });
}

$('btn-close-settings').addEventListener('click', () => { factorPassword = null; });

function renderSyncCard() {
  const box = $('sy-card');
  const s = app.sync;
  if (!box || !s) return;
  const rows = [
    ['ฐานข้อมูลในเครื่อง', 'พร้อม (%APPDATA%\\PrePack\\prepack-local.db)'],
    ['MySQL', s.configured ? (s.lastError ? '<span class="err-text">' + esc(s.lastError) + '</span>' : 'ตั้งค่าแล้ว') : 'ยังไม่ได้ตั้งค่า — บันทึกในเครื่องไปก่อน'],
    ['sync ล่าสุด', s.lastSyncAt ? fmtThaiDate(s.lastSyncAt.slice(0, 10)) + ' ' + s.lastSyncAt.slice(11, 16) : '-'],
    ['รอส่งขึ้น MySQL', s.unsynced + ' รายการ'],
  ];
  box.innerHTML = rows.map(([k, v]) => '<div class="muted">' + k + '</div><div>' + v + '</div>').join('');
}

// ── Database connections ──

TAB_LOADERS.db = function () {
  const m = app.config.mysql, v = app.config.invs;
  const pw = has => has ? 'placeholder="(บันทึกไว้แล้ว — เว้นว่าง = ใช้รหัสเดิม)"' : '';
  $('tab-db').innerHTML =
    '<div class="card"><h3>การบันทึกและ sync</h3>' +
    '<p class="note" style="margin-top:0">ทุกการพิมพ์บันทึกลงฐานข้อมูลในเครื่องนี้ก่อนเสมอ (SQLite) แล้ว sync ขึ้น MySQL ' +
    'อัตโนมัติทุก 2 นาที · ข้อมูลในเครื่องเก็บไว้ต่อเป็น backup ไม่ถูกลบ</p>' +
    '<div id="sy-card" class="kv"></div>' +
    '<div class="row" style="margin-top:12px"><button class="primary" id="sy-now">Sync ตอนนี้</button>' +
    '<button id="sy-resync">ส่งข้อมูลในเครื่องขึ้นใหม่ทั้งหมด (กู้คืน)</button></div>' +
    '<div id="sy-msg" class="form-msg"></div></div>' +

    '<div class="card"><h3>MySQL — ฐานข้อมูลกลางของ PrePack (รวมทุกเครื่อง)</h3>' +
    '<div class="grid">' +
    '<label class="fld">Host<input id="my-host" value="' + esc(m.host) + '"></label>' +
    '<label class="fld">Port<input id="my-port" type="number" value="' + m.port + '"></label>' +
    '<label class="fld">User<input id="my-user" value="' + esc(m.user) + '"></label>' +
    '<label class="fld">Password<input id="my-pass" type="password" ' + pw(m.hasPassword) + '></label>' +
    '<label class="fld">Database<input id="my-db" value="' + esc(m.database) + '"></label></div>' +
    '<p class="note">กดบันทึกแล้วโปรแกรมจะสร้างฐานข้อมูลและตารางให้เอง (CREATE DATABASE IF NOT EXISTS + migration) ' +
    'ต้องเป็นฐานแยกจาก HOSxP / JHCIS — ถ้าเจอตารางของ HOSxP/JHCIS โปรแกรมจะไม่เขียน</p>' +
    '<div class="row" style="margin-top:10px"><button class="primary" id="my-save">ทดสอบ + สร้างตาราง + บันทึก</button></div>' +
    '<div id="my-msg" class="form-msg"></div></div>' +

    '<div class="card"><h3>INVS — SQL Server (อ่านอย่างเดียว: ค้นหายา, Lot, EXP)</h3>' +
    '<div class="row" style="margin-bottom:10px"><button id="iv-find">หา invs.ini อัตโนมัติ</button>' +
    '<button id="iv-browse">เลือกไฟล์ invs.ini…</button><span class="muted" id="iv-ini">' + esc(v.iniPath) + '</span></div>' +
    '<div class="grid">' +
    '<label class="fld">Server<input id="iv-host" value="' + esc(v.host) + '"></label>' +
    '<label class="fld">Port<input id="iv-port" type="number" value="' + v.port + '"></label>' +
    '<label class="fld">Database<input id="iv-db" value="' + esc(v.database) + '"></label>' +
    '<label class="fld">User<input id="iv-user" value="' + esc(v.user) + '"></label>' +
    '<label class="fld">Password<input id="iv-pass" type="password" ' + pw(v.hasPassword) + '></label>' +
    '<label class="fld">คอลัมน์หน่วยใน DRUG_GN<select id="iv-unit"><option value="">(auto-detect)</option>' +
    (v.unitColumn ? '<option selected>' + esc(v.unitColumn) + '</option>' : '') + '</select></label></div>' +
    '<p class="note">ยังไม่รู้ชื่อคอลัมน์หน่วยของ INVS — กดทดสอบแล้วโปรแกรมจะหาคอลัมน์ที่มีคำว่า UNIT / DOSAGE / FORM ให้เลือก</p>' +
    '<div class="row" style="margin-top:10px"><button class="primary" id="iv-save">ทดสอบ + บันทึก</button></div>' +
    '<div id="iv-msg" class="form-msg"></div></div>';

  const fillInvs = cfg => {
    const i = cfg.invs;
    $('iv-host').value = i.host; $('iv-port').value = i.port; $('iv-db').value = i.database; $('iv-user').value = i.user;
    $('iv-pass').value = ''; $('iv-pass').placeholder = i.hasPassword ? '(บันทึกไว้แล้ว — เว้นว่าง = ใช้รหัสเดิม)' : '';
    $('iv-ini').textContent = i.iniPath;
  };
  const fillUnitColumns = (cols, cur) => {
    $('iv-unit').innerHTML = '<option value="">(auto-detect)</option>' +
      cols.map(c => '<option' + (c === cur ? ' selected' : '') + '>' + esc(c) + '</option>').join('');
  };

  $('my-save').addEventListener('click', async () => {
    formMsg('my-msg', 'กำลังเชื่อมต่อ…');
    try {
      const r = await call('SaveMySql', JSON.stringify({
        host: $('my-host').value, port: Number($('my-port').value), user: $('my-user').value,
        password: $('my-pass').value, database: $('my-db').value,
      }));
      onConfigChanged(r.config, r.staff, r.workFactors);
      setSyncStatus(r.sync);
      renderSyncCard();
      $('my-pass').value = '';
      formMsg('my-msg', 'เชื่อมต่อได้ · ' + (r.applied.length ? 'สร้าง/อัปเดตตาราง: ' + r.applied.join(', ') : 'ตารางครบแล้ว') +
        ' · sync ข้อมูลในเครื่องขึ้นแล้ว', 'ok');
    } catch (e) { formMsg('my-msg', e.message, 'err'); }
  });
  $('sy-now').addEventListener('click', async () => {
    formMsg('sy-msg', 'กำลัง sync…');
    try {
      const r = await call('SyncNow');
      onConfigChanged(null, r.staff, r.workFactors);
      setSyncStatus(r.sync); renderSyncCard();
      formMsg('sy-msg', 'sync เรียบร้อย', 'ok');
    } catch (e) { formMsg('sy-msg', e.message, 'err'); pollSync().then(renderSyncCard); }
  });
  $('sy-resync').addEventListener('click', async () => {
    if (!confirm('ส่งข้อมูลการพิมพ์ทั้งหมดในเครื่องนี้ขึ้น MySQL ใหม่?\n(ใช้กู้คืนเมื่อ server ถูกล้าง — รายการที่มีอยู่แล้วจะไม่ซ้ำ)')) return;
    formMsg('sy-msg', 'กำลังส่ง…');
    try {
      const r = await call('ResyncAll');
      setSyncStatus(r.sync); renderSyncCard();
      formMsg('sy-msg', r.sync.lastError ? r.sync.lastError : 'ส่งขึ้นใหม่ ' + r.queued + ' รายการแล้ว', r.sync.lastError ? 'err' : 'ok');
    } catch (e) { formMsg('sy-msg', e.message, 'err'); }
  });
  renderSyncCard();
  $('iv-find').addEventListener('click', async () => {
    try { const r = await call('FindInvsIni'); onConfigChanged(r.config); fillInvs(r.config); formMsg('iv-msg', 'อ่าน invs.ini แล้ว — กดทดสอบ + บันทึก', 'ok'); }
    catch (e) { formMsg('iv-msg', e.message, 'err'); }
  });
  $('iv-browse').addEventListener('click', async () => {
    try {
      const r = await call('BrowseInvsIni');
      if (r.cancelled) return;
      onConfigChanged(r.config); fillInvs(r.config); formMsg('iv-msg', 'อ่าน invs.ini แล้ว — กดทดสอบ + บันทึก', 'ok');
    } catch (e) { formMsg('iv-msg', e.message, 'err'); }
  });
  $('iv-save').addEventListener('click', async () => {
    formMsg('iv-msg', 'กำลังเชื่อมต่อ…');
    try {
      const r = await call('SaveInvs', JSON.stringify({
        host: $('iv-host').value, port: Number($('iv-port').value), database: $('iv-db').value,
        user: $('iv-user').value, password: $('iv-pass').value, unitColumn: $('iv-unit').value,
      }));
      onConfigChanged(r.config);
      fillUnitColumns(r.unitColumns, r.config.invs.unitColumn);
      $('iv-pass').value = '';
      formMsg('iv-msg', 'เชื่อมต่อ INVS ได้ · คอลัมน์หน่วย: ' + (r.config.invs.unitColumn || 'ไม่พบ (ใช้ชื่อยาตัดสินอย่างเดียว)'), 'ok');
    } catch (e) { formMsg('iv-msg', e.message, 'err'); }
  });
};
