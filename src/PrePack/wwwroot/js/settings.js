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
        (s.active ? 'ใช้งาน' : '<span class="muted">ปิด</span>') + '</td><td><button class="small" data-edit="' + s.id +
        '">แก้ไข</button></td></tr>').join('') + '</tbody></table>';
  };
  list();

  $('st-list').addEventListener('click', e => {
    const id = e.target.dataset.edit;
    if (!id) return;
    const s = app.staff.find(x => String(x.id) === id);
    $('st-id').value = s.id; $('st-name').value = s.name; $('st-short').value = s.shortName;
    $('st-active').checked = s.active; $('st-active-wrap').hidden = false; $('st-title').textContent = 'แก้ไขเจ้าหน้าที่';
    $('st-name').focus();
  });
  $('st-reset').addEventListener('click', () => { reset(); formMsg('st-msg', ''); });
  $('st-save').addEventListener('click', async () => {
    try {
      const staff = await call('SaveStaff', JSON.stringify({
        id: Number($('st-id').value) || 0, name: $('st-name').value, shortName: $('st-short').value,
        active: $('st-id').value ? $('st-active').checked : true,
      }));
      onConfigChanged(null, staff);
      reset(); list();
      formMsg('st-msg', 'บันทึกแล้ว', 'ok');
    } catch (e) { formMsg('st-msg', e.message, 'err'); }
  });
};

// ── Sticker layout + printer ──

const LAYOUT_FIELDS = [
  ['frameWidth', 'กว้างแผ่น (มม.)', 0.1], ['frameHeight', 'สูงแผ่น (มม.)', 0.1], ['headerHeight', 'ส่วนหัวที่ฉีกทิ้ง (มม.)', 0.1],
  ['rows', 'จำนวนแถว', 1], ['cols', 'จำนวนคอลัมน์', 1], ['fontSize', 'ขนาดอักษร (pt)', 0.1],
  ['gapX', 'ช่องว่างแนวนอน (มม.)', 0.1], ['gapY', 'ช่องว่างแนวตั้ง (มม.)', 0.1], ['padding', 'ขอบในดวง (มม.)', 0.1],
  ['offsetX', 'เลื่อนขวา + / ซ้าย − (มม.)', 0.1], ['offsetY', 'เลื่อนลง + / ขึ้น − (มม.)', 0.1],
];

TAB_LOADERS.label = async function () {
  const l = app.config.layout;
  $('tab-label').innerHTML =
    '<div class="card"><h3>เครื่องพิมพ์</h3><div class="row"><select id="lb-printer" style="max-width:420px"></select>' +
    '<button id="lb-refresh" class="small">โหลดรายชื่อใหม่</button></div>' +
    '<p class="note">พิมพ์แบบ silent ไปที่เครื่องนี้โดยตรง ขนาดกระดาษ = ขนาดแผ่นด้านล่าง ไม่มีขอบ ไม่มี header/footer</p></div>' +
    '<div class="card"><h3>ขนาดสติกเกอร์</h3><div class="grid">' +
    LAYOUT_FIELDS.map(([k, t, step]) => '<label class="fld">' + t + '<input type="number" step="' + step + '" id="lb-' + k + '" value="' + l[k] + '"></label>').join('') +
    '<label class="check"><input type="checkbox" id="lb-buddhistYear"' + (l.buddhistYear ? ' checked' : '') + '> ปี พ.ศ.</label>' +
    '</div><div class="row" style="margin-top:12px"><button class="primary" id="lb-save">บันทึก</button>' +
    '<button id="lb-default">ค่าเริ่มต้น 85×50 มม. 3×3</button></div><div id="lb-msg" class="form-msg"></div></div>' +
    '<div class="card"><h3>ตัวอย่าง <span class="muted" id="lb-cell"></span></h3><div id="lb-preview" class="label-preview"></div>' +
    '<p class="note">กด "ทดสอบตำแหน่ง" ที่หน้าหลักเพื่อพิมพ์เส้นขอบ ถ้าเลื่อน ให้แก้ค่า "เลื่อนขวา / เลื่อนลง"</p></div>';

  const read = () => {
    const o = {};
    LAYOUT_FIELDS.forEach(([k]) => { o[k] = Number($('lb-' + k).value); });
    o.buddhistYear = $('lb-buddhistYear').checked;
    return o;
  };
  const preview = () => {
    const o = read();
    const err = validateLayout(o);
    if (err) { $('lb-preview').innerHTML = '<p class="form-msg err">' + esc(err) + '</p>'; $('lb-cell').textContent = ''; return; }
    const sample = { drugName: 'Amoxicillin/Clavulanate 1 g (Augmentin)', qty: 100, lotNo: 'A12345',
      packer: 'สมชาย ใจดีมากมาก', packDate: app.today, labelExp: addDaysISO(app.today, 365) };
    $('lb-preview').innerHTML = buildFrames(sample, o, o.rows * o.cols, true);
    $('lb-cell').textContent = '· ขนาดดวง ' + fmtNum(cellWidth(o)) + ' × ' + fmtNum(cellHeight(o)) + ' มม.';
  };
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
    const d = { frameWidth: 85, frameHeight: 50, headerHeight: 8, rows: 3, cols: 3, gapX: 1, gapY: 1,
      offsetX: 0, offsetY: 0, padding: 0.8, fontSize: 5.5 };
    Object.keys(d).forEach(k => { $('lb-' + k).value = d[k]; });
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
      ? '<div class="row"><input type="password" id="wf-pass" placeholder="รหัสสำหรับแก้ไข" style="width:200px">' +
        '<button id="wf-unlock">🔒 ปลดล็อกเพื่อแก้ไข</button></div>'
      : '<div class="row"><span class="tag">ปลดล็อกแล้ว</span><button class="primary" id="wf-save">บันทึก</button>' +
        '<button id="wf-lock">ล็อก</button></div>') +
    '<div id="wf-msg" class="form-msg"></div></div>' + byType;

  const box = $('tab-factors');
  box.onclick = async e => {
    const t = e.target;
    if (t.id === 'wf-unlock') {
      const pw = $('wf-pass').value;
      try { await call('CheckAdminPassword', pw); factorPassword = pw; renderFactors(readFactors(table)); }
      catch (err) { formMsg('wf-msg', err.message, 'err'); }
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
  if ($('wf-pass')) $('wf-pass').onkeydown = e => { if (e.key === 'Enter') $('wf-unlock').click(); };
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

// ── Database connections ──

TAB_LOADERS.db = function () {
  const m = app.config.mysql, v = app.config.invs;
  const pw = has => has ? 'placeholder="(บันทึกไว้แล้ว — เว้นว่าง = ใช้รหัสเดิม)"' : '';
  $('tab-db').innerHTML =
    '<div class="card"><h3>MySQL — ฐานข้อมูลของ PrePack (เจ้าหน้าที่ + บันทึกภาระงาน)</h3>' +
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
      app.dbReady = true;
      onConfigChanged(r.config, r.staff, r.workFactors);
      setDbStatus(null, 0);
      $('my-pass').value = '';
      formMsg('my-msg', 'เชื่อมต่อได้ · ' + (r.applied.length ? 'สร้าง/อัปเดตตาราง: ' + r.applied.join(', ') : 'ตารางครบแล้ว'), 'ok');
    } catch (e) { formMsg('my-msg', e.message, 'err'); }
  });
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
