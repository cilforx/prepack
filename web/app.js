// PrePack main UI. Plain JS; state is loaded from the API.

const state = { staff: [], drugs: [], layout: null, sizeId: null, report: null };
const $ = id => document.getElementById(id);

function setMsg(el, text, ok) {
  el.textContent = text || '';
  el.className = 'msg ' + (text ? (ok ? 'ok' : 'err') : '');
}

function lsGet(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
function lsSet(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* ignore */ } }

function openPrint(jobId) {
  window.open('print.html?job=' + jobId, '_blank');
}

// ── Tabs ──
document.querySelectorAll('#tabs button').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('#tabs button').forEach(b => b.classList.toggle('active', b === btn));
    document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.id === 'tab-' + btn.dataset.tab));
    const loaders = { jobs: loadJobs, report: loadReport, label: renderLabelPreview };
    if (loaders[btn.dataset.tab]) loaders[btn.dataset.tab]();
  });
});

// ── Master data ──
async function loadStaff() {
  state.staff = await api('/staff?all=1');
  const active = state.staff.filter(s => s.active);
  const opts = active.map(s => '<option value="' + s.id + '">' + esc(s.name) + '</option>').join('');
  const packer = $('pk-packer');
  packer.innerHTML = '<option value="">— เลือก —</option>' + opts;
  packer.value = lsGet('prepack_packer') || '';
  $('jf-packer').innerHTML = '<option value="">ทั้งหมด</option>' + opts;
  const checkers = active.filter(s => s.role === 'pharmacist').concat(active.filter(s => s.role !== 'pharmacist'));
  $('jf-checker').innerHTML = '<option value="">— เลือกผู้ตรวจ —</option>' +
    checkers.map(s => '<option value="' + s.id + '">' + esc(s.name) + '</option>').join('');
  $('jf-checker').value = lsGet('prepack_checker') || '';
  renderStaffList();
}

async function loadDrugs() {
  state.drugs = await api('/drugs?all=1');
  renderDrugPicker();
  renderDrugList();
}

// ── Pack form ──
function activeDrugs() { return state.drugs.filter(d => d.active && d.sizes.some(s => s.active)); }

function renderDrugPicker() {
  const q = $('pk-drug-search').value.trim().toLowerCase();
  const sel = $('pk-drug');
  const prev = sel.value;
  const list = activeDrugs().filter(d =>
    !q || (d.name + ' ' + d.strength + ' ' + (d.his_code || '')).toLowerCase().includes(q));
  sel.innerHTML = list.map(d =>
    '<option value="' + d.id + '">' + esc(d.name + ' ' + d.strength) + '</option>').join('');
  if (list.some(d => String(d.id) === prev)) sel.value = prev;
  else if (list.length === 1) sel.value = String(list[0].id);
  renderSizes();
}

function selectedDrug() { return state.drugs.find(d => String(d.id) === $('pk-drug').value); }
function selectedSize() {
  const d = selectedDrug();
  return d ? d.sizes.find(s => s.id === state.sizeId && s.active) : null;
}

function renderSizes() {
  const d = selectedDrug();
  const box = $('pk-sizes');
  if (!d) { box.innerHTML = '<span class="muted">เลือกยาก่อน</span>'; state.sizeId = null; renderSummary(); return; }
  const sizes = d.sizes.filter(s => s.active);
  if (!sizes.some(s => s.id === state.sizeId)) state.sizeId = sizes.length === 1 ? sizes[0].id : null;
  box.innerHTML = sizes.map(s =>
    '<button type="button" class="chip' + (s.id === state.sizeId ? ' sel' : '') + '" data-id="' + s.id + '">' +
    fmtNum(s.qty) + ' ' + esc(d.unit) + ' <small>' + PACK_TYPE_LABEL[s.pack_type] + '</small></button>').join('');
  renderSummary();
}

$('pk-sizes').addEventListener('click', e => {
  const b = e.target.closest('.chip');
  if (!b) return;
  state.sizeId = Number(b.dataset.id);
  renderSizes();
});
$('pk-drug-search').addEventListener('input', renderDrugPicker);
$('pk-drug').addEventListener('change', () => { state.sizeId = null; renderSizes(); });
['pk-count', 'pk-srcexp', 'pk-date'].forEach(id => $(id).addEventListener('input', renderSummary));
$('pk-packer').addEventListener('change', () => lsSet('prepack_packer', $('pk-packer').value));

// Client-side preview only; the server recomputes the BUD authoritatively.
function renderSummary() {
  const d = selectedDrug(), s = selectedSize();
  const count = parseInt($('pk-count').value, 10);
  const src = $('pk-srcexp').value, pd = $('pk-date').value;
  if (!d || !s || !count) { $('pk-summary').innerHTML = ''; return; }
  let html = 'รวม <b>' + fmtNum(s.qty * count) + ' ' + esc(d.unit) + '</b> (' + count + ' ซอง × ' + fmtNum(s.qty) + ')' +
    ' · แต้มงาน ' + fmtNum(s.work_point * count);
  if (src && pd) {
    if (src <= pd) {
      html += '<br><span class="msg err">ยาในภาชนะเดิมหมดอายุแล้ว</span>';
    } else {
      let bud = addDaysISO(pd, d.bud_days);
      if (src < bud) bud = src;
      html += '<br>EXP หลังแบ่งบรรจุ: <b>' + fmtThaiDate(bud) + '</b> <span class="muted">(' + d.bud_days +
        ' วัน หรือ EXP เดิม แล้วแต่ถึงก่อน)</span>';
    }
  }
  $('pk-summary').innerHTML = html;
}

$('pack-form').addEventListener('submit', async e => {
  e.preventDefault();
  const msg = $('pk-msg');
  const s = selectedSize();
  if (!s) { setMsg(msg, 'กรุณาเลือกขนาดบรรจุ'); return; }
  try {
    const job = await api('/jobs', { method: 'POST', body: {
      drug_id: Number($('pk-drug').value),
      pack_size_id: s.id,
      pack_count: parseInt($('pk-count').value, 10),
      lot_no: $('pk-lot').value,
      src_exp_date: $('pk-srcexp').value,
      pack_date: $('pk-date').value,
      packer_id: Number($('pk-packer').value),
      note: $('pk-note').value
    } });
    setMsg(msg, 'บันทึกแล้ว #' + job.id + ' — EXP ' + fmtThaiDate(job.bud_date), true);
    openPrint(job.id);
    $('pk-count').value = '';
    $('pk-note').value = '';
    renderSummary();
    loadToday();
  } catch (err) {
    setMsg(msg, err.message);
  }
});

async function loadToday() {
  const today = $('pk-date').value || todayISO();
  const jobs = await api('/jobs?from=' + today + '&to=' + today);
  const live = jobs.filter(j => j.status !== 'cancelled');
  $('today-total').textContent = live.length + ' รายการ · ' + live.reduce((a, j) => a + j.pack_count, 0) + ' ซอง';
  $('today-jobs').innerHTML = jobsTable(jobs, false);
}

// ── Jobs table ──
function jobsTable(jobs, withActions) {
  if (!jobs.length) return '<p class="muted">ไม่มีรายการ</p>';
  let h = '<div class="table-wrap"><table><thead><tr><th>#</th><th>วันบรรจุ</th><th>ยา</th><th class="num">ขนาด</th>' +
    '<th class="num">ซอง</th><th>Lot</th><th>EXP ใหม่</th><th>ผู้บรรจุ</th><th>สถานะ</th><th></th></tr></thead><tbody>';
  jobs.forEach(j => {
    h += '<tr class="' + j.status + '"><td>' + j.id + '</td><td>' + fmtThaiDate(j.pack_date) + '</td>' +
      '<td>' + esc(j.drug_name + ' ' + j.strength) + '<br><span class="muted">' + PACK_TYPE_LABEL[j.pack_type] + '</span></td>' +
      '<td class="num">' + fmtNum(j.qty_per_pack) + ' ' + esc(j.unit) + '</td><td class="num">' + j.pack_count + '</td>' +
      '<td>' + esc(j.lot_no) + '</td><td>' + fmtThaiDate(j.bud_date) + '</td><td>' + esc(j.packer_name) + '</td>' +
      '<td><span class="badge ' + j.status + '">' + STATUS_LABEL[j.status] + '</span>' +
      (j.checker_name ? '<br><span class="muted">' + esc(j.checker_name) + '</span>' : '') +
      (j.note ? '<br><span class="muted">' + esc(j.note) + '</span>' : '') + '</td><td>';
    if (j.status !== 'cancelled') h += '<button class="small" data-act="print" data-id="' + j.id + '">พิมพ์</button> ';
    if (withActions && j.status === 'packed') h += '<button class="small" data-act="check" data-id="' + j.id + '">ตรวจ ✓</button> ';
    if (withActions && j.status !== 'cancelled') h += '<button class="small danger" data-act="cancel" data-id="' + j.id + '">ยกเลิก</button>';
    h += '</td></tr>';
  });
  return h + '</tbody></table></div>';
}

async function onJobAction(e) {
  const b = e.target.closest('button[data-act]');
  if (!b) return;
  const id = b.dataset.id;
  try {
    if (b.dataset.act === 'print') {
      openPrint(id);
    } else if (b.dataset.act === 'check') {
      const checker = $('jf-checker').value;
      if (!checker) { alert('เลือกผู้ตรวจที่ช่อง "ผู้ตรวจ" ด้านบนก่อน'); return; }
      await api('/jobs/' + id + '/check', { method: 'POST', body: { checker_id: Number(checker) } });
      loadJobs();
    } else if (b.dataset.act === 'cancel') {
      const reason = prompt('เหตุผลที่ยกเลิก');
      if (!reason) return;
      await api('/jobs/' + id + '/cancel', { method: 'POST', body: { reason } });
      loadJobs();
    }
  } catch (err) {
    alert(err.message);
  }
}
$('jobs-table').addEventListener('click', onJobAction);
$('today-jobs').addEventListener('click', onJobAction);
$('jf-checker').addEventListener('change', () => lsSet('prepack_checker', $('jf-checker').value));

async function loadJobs() {
  const p = new URLSearchParams();
  ['from', 'to', 'packer', 'status', 'lot'].forEach(k => {
    const v = $('jf-' + k).value.trim();
    if (v) p.set(k === 'packer' ? 'packer_id' : k, v);
  });
  try {
    $('jobs-table').innerHTML = jobsTable(await api('/jobs?' + p.toString()), true);
  } catch (err) {
    $('jobs-table').innerHTML = '<p class="msg err">' + esc(err.message) + '</p>';
  }
}
$('jf-go').addEventListener('click', loadJobs);

// ── Workload report ──
async function loadReport() {
  try {
    const r = await api('/reports/workload?from=' + $('rp-from').value + '&to=' + $('rp-to').value);
    state.report = r;
    const max = Math.max(1, ...r.rows.map(x => x.points));
    let h = '<div class="table-wrap"><table><thead><tr><th>เจ้าหน้าที่</th><th class="num">รายการ</th>' +
      '<th class="num">Pre-pack</th><th class="num">Unit dose</th><th class="num">ครีม</th><th class="num">รวมซอง</th>' +
      '<th class="num">แต้มงาน</th><th style="width:30%"></th><th class="num">ตรวจให้ผู้อื่น</th></tr></thead><tbody>';
    r.rows.forEach(x => {
      h += '<tr><td>' + esc(x.name) + '</td><td class="num">' + x.jobs + '</td><td class="num">' + x.prepack_packs +
        '</td><td class="num">' + x.unitdose_packs + '</td><td class="num">' + x.cream_packs + '</td><td class="num">' +
        x.packs + '</td><td class="num"><b>' + fmtNum(x.points) + '</b></td><td><div class="bar" style="width:' +
        (x.points / max * 100) + '%"></div></td><td class="num">' + x.checked_for + '</td></tr>';
    });
    $('report-table').innerHTML = h + '</tbody></table></div>';
  } catch (err) {
    $('report-table').innerHTML = '<p class="msg err">' + esc(err.message) + '</p>';
  }
}
$('rp-go').addEventListener('click', loadReport);
$('rp-csv').addEventListener('click', () => {
  const r = state.report;
  if (!r) return;
  const q = v => '"' + String(v).replace(/"/g, '""') + '"';
  const lines = [['เจ้าหน้าที่', 'รายการ', 'Pre-pack', 'Unit dose', 'ครีม', 'รวมซอง', 'แต้มงาน', 'ตรวจให้ผู้อื่น'].map(q).join(',')];
  r.rows.forEach(x => lines.push([x.name, x.jobs, x.prepack_packs, x.unitdose_packs, x.cream_packs, x.packs, x.points, x.checked_for].map(q).join(',')));
  const blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'prepack-workload-' + r.from + '_' + r.to + '.csv';
  a.click();
  URL.revokeObjectURL(a.href);
});

// ── Drug master ──
function renderDrugList() {
  const all = $('dg-show-all').checked;
  const list = state.drugs.filter(d => all || d.active);
  if (!list.length) { $('drugs-list').innerHTML = '<p class="muted">ยังไม่มีข้อมูลยา</p>'; return; }
  $('drugs-list').innerHTML = list.map(d => {
    const sizes = d.sizes.map(s =>
      '<span class="size-row' + (s.active ? '' : ' inactive') + '">' + PACK_TYPE_LABEL[s.pack_type] + ' ' +
      fmtNum(s.qty) + ' ' + esc(d.unit) + ' · แต้ม <input type="number" step="0.1" min="0" value="' + s.work_point +
      '" data-size="' + s.id + '" data-active="' + s.active + '">' +
      '<button type="button" class="small" data-toggle-size="' + s.id + '" data-active="' + s.active + '">' +
      (s.active ? 'ปิด' : 'เปิด') + '</button></span>').join('');
    return '<div class="drug-item' + (d.active ? '' : ' inactive') + '"><div class="head"><div><b>' +
      esc(d.name + ' ' + d.strength) + '</b> <span class="muted">' + esc(d.his_code || '') + ' · อายุหลังแบ่ง ' +
      d.bud_days + ' วัน</span></div><button type="button" class="small" data-edit-drug="' + d.id + '">แก้ไข</button></div>' +
      '<div>' + sizes + '</div><div class="row" style="margin-top:4px"><select data-new-type="' + d.id + '">' +
      '<option value="prepack">Pre-pack</option><option value="unitdose">Unit dose</option><option value="cream">แบ่งครีม</option>' +
      '</select><input type="number" step="0.01" min="0" placeholder="จำนวน" style="width:80px" data-new-qty="' + d.id + '">' +
      '<button type="button" class="small" data-add-size="' + d.id + '">+ เพิ่มขนาด</button></div></div>';
  }).join('');
}
$('dg-show-all').addEventListener('change', renderDrugList);

$('drugs-list').addEventListener('click', async e => {
  const t = e.target;
  try {
    if (t.dataset.editDrug) {
      const d = state.drugs.find(x => String(x.id) === t.dataset.editDrug);
      $('dg-id').value = d.id; $('dg-name').value = d.name; $('dg-strength').value = d.strength;
      $('dg-code').value = d.his_code || ''; $('dg-form').value = d.form; $('dg-unit').value = d.unit;
      $('dg-bud').value = d.bud_days; $('dg-active').checked = d.active;
      $('dg-active-wrap').hidden = false; $('dg-sizes-new').hidden = true;
      $('drug-form-title').textContent = 'แก้ไขยา';
      $('dg-name').focus();
    } else if (t.dataset.toggleSize) {
      const input = document.querySelector('input[data-size="' + t.dataset.toggleSize + '"]');
      await api('/sizes/' + t.dataset.toggleSize, { method: 'PUT',
        body: { work_point: Number(input.value), active: t.dataset.active !== 'true' } });
      await loadDrugs();
    } else if (t.dataset.addSize) {
      const id = t.dataset.addSize;
      const qty = Number(document.querySelector('input[data-new-qty="' + id + '"]').value);
      const type = document.querySelector('select[data-new-type="' + id + '"]').value;
      await api('/drugs/' + id + '/sizes', { method: 'POST', body: { pack_type: type, qty, work_point: defaultPoint(type, qty) } });
      await loadDrugs();
    }
  } catch (err) {
    alert(err.message);
  }
});

$('drugs-list').addEventListener('change', async e => {
  const t = e.target;
  if (!t.dataset.size) return;
  try {
    await api('/sizes/' + t.dataset.size, { method: 'PUT', body: { work_point: Number(t.value), active: t.dataset.active === 'true' } });
    await loadDrugs();
  } catch (err) {
    alert(err.message);
  }
});

// Starting work point per pack: counting tablets scales with quantity; unit dose and
// cream have a fixed per-pack handling cost. Adjust per drug in the list.
function defaultPoint(type, qty) {
  if (type === 'prepack') return Math.max(1, Math.round(qty / 30 * 10) / 10);
  return 1;
}

// "prepack:30,60 unitdose:1,2 cream:5" -> [{pack_type, qty, work_point}]
function parseSizes(text) {
  const out = [];
  text.trim().split(/\s+/).filter(Boolean).forEach(part => {
    const [type, nums] = part.split(':');
    if (!PACK_TYPE_LABEL[type] || !nums) throw new Error('รูปแบบขนาดบรรจุไม่ถูกต้อง: ' + part);
    nums.split(',').filter(Boolean).forEach(n => {
      const qty = Number(n);
      if (!(qty > 0)) throw new Error('จำนวนไม่ถูกต้อง: ' + n);
      out.push({ pack_type: type, qty, work_point: defaultPoint(type, qty) });
    });
  });
  return out;
}

function resetDrugForm() {
  $('drug-form').reset();
  $('dg-id').value = '';
  $('dg-active-wrap').hidden = true; $('dg-sizes-new').hidden = false;
  $('drug-form-title').textContent = 'เพิ่มยา';
}
$('dg-reset').addEventListener('click', () => { resetDrugForm(); setMsg($('dg-msg'), ''); });
$('dg-form').addEventListener('change', () => {
  if (!$('dg-unit').value) $('dg-unit').placeholder = $('dg-form').value === 'cream' ? 'g' : 'เม็ด';
});

$('drug-form').addEventListener('submit', async e => {
  e.preventDefault();
  const id = $('dg-id').value;
  const body = {
    name: $('dg-name').value, strength: $('dg-strength').value, his_code: $('dg-code').value || null,
    form: $('dg-form').value, unit: $('dg-unit').value, bud_days: parseInt($('dg-bud').value, 10)
  };
  try {
    if (id) {
      body.active = $('dg-active').checked;
      await api('/drugs/' + id, { method: 'PUT', body });
    } else {
      body.sizes = parseSizes($('dg-sizes').value);
      await api('/drugs', { method: 'POST', body });
    }
    setMsg($('dg-msg'), 'บันทึกแล้ว', true);
    resetDrugForm();
    await loadDrugs();
  } catch (err) {
    setMsg($('dg-msg'), err.message);
  }
});

// ── Staff ──
const ROLE_LABEL = { packer: 'ผู้บรรจุ', pharmacist: 'เภสัชกร' };
function renderStaffList() {
  if (!state.staff.length) { $('staff-list').innerHTML = '<p class="muted">ยังไม่มีเจ้าหน้าที่</p>'; return; }
  $('staff-list').innerHTML = '<table><thead><tr><th>ชื่อ</th><th>บนฉลาก</th><th>ตำแหน่ง</th><th>สถานะ</th><th></th></tr></thead><tbody>' +
    state.staff.map(s => '<tr><td>' + esc(s.name) + '</td><td>' + esc(s.short_name) + '</td><td>' + ROLE_LABEL[s.role] +
      '</td><td>' + (s.active ? 'ใช้งาน' : '<span class="muted">ปิด</span>') + '</td><td><button class="small" data-edit-staff="' +
      s.id + '">แก้ไข</button></td></tr>').join('') + '</tbody></table>';
}
$('staff-list').addEventListener('click', e => {
  const id = e.target.dataset.editStaff;
  if (!id) return;
  const s = state.staff.find(x => String(x.id) === id);
  $('sf-id').value = s.id; $('sf-name').value = s.name; $('sf-short').value = s.short_name;
  $('sf-code').value = s.code || ''; $('sf-role').value = s.role; $('sf-active').checked = s.active;
  $('sf-active-wrap').hidden = false; $('staff-form-title').textContent = 'แก้ไขเจ้าหน้าที่';
});
function resetStaffForm() {
  $('staff-form').reset(); $('sf-id').value = '';
  $('sf-active-wrap').hidden = true; $('staff-form-title').textContent = 'เพิ่มเจ้าหน้าที่';
}
$('sf-reset').addEventListener('click', () => { resetStaffForm(); setMsg($('sf-msg'), ''); });
$('staff-form').addEventListener('submit', async e => {
  e.preventDefault();
  const id = $('sf-id').value;
  const body = { name: $('sf-name').value, short_name: $('sf-short').value, code: $('sf-code').value || null, role: $('sf-role').value };
  try {
    if (id) {
      body.active = $('sf-active').checked;
      await api('/staff/' + id, { method: 'PUT', body });
    } else {
      await api('/staff', { method: 'POST', body });
    }
    setMsg($('sf-msg'), 'บันทึกแล้ว', true);
    resetStaffForm();
    await loadStaff();
  } catch (err) {
    setMsg($('sf-msg'), err.message);
  }
});

// ── Label layout ──
const LAYOUT_NUM = ['frame_width', 'frame_height', 'header_height', 'rows', 'cols', 'gap_x', 'gap_y', 'offset_x', 'offset_y', 'padding', 'font_size'];
const LAYOUT_BOOL = ['buddhist_year', 'show_packer'];
const DEFAULT_LAYOUT = { frame_width: 85, frame_height: 50, header_height: 8, rows: 3, cols: 3, gap_x: 1, gap_y: 1,
  offset_x: 0, offset_y: 0, padding: 0.8, font_size: 5.5, buddhist_year: true, show_packer: true };

function fillLayoutForm(l) {
  LAYOUT_NUM.forEach(k => { $('lb-' + k).value = l[k]; });
  LAYOUT_BOOL.forEach(k => { $('lb-' + k).checked = !!l[k]; });
}
function readLayoutForm() {
  const l = {};
  LAYOUT_NUM.forEach(k => { l[k] = Number($('lb-' + k).value); });
  LAYOUT_BOOL.forEach(k => { l[k] = $('lb-' + k).checked; });
  return l;
}

const SAMPLE_JOB = { drug_name: 'Paracetamol', strength: '500 mg', unit: 'เม็ด', qty_per_pack: 30, lot_no: 'A12345',
  pack_date: todayISO(), bud_date: addDaysISO(todayISO(), 180), packer_short: 'สมชาย' };

function renderLabelPreview() {
  const l = readLayoutForm();
  const scale = 2;
  $('lb-preview').innerHTML = '<div style="width:' + (l.frame_width * scale) + 'mm;height:' + (l.frame_height * scale) + 'mm">' +
    buildFrames(SAMPLE_JOB, l, l.rows * l.cols, 0, true) + '</div>';
  $('lb-cell-size').textContent = 'ขนาดดวง: ' + fmtNum(cellWidth(l)) + ' × ' + fmtNum(cellHeight(l)) + ' มม.';
}
$('label-form').addEventListener('input', renderLabelPreview);
$('lb-default').addEventListener('click', () => { fillLayoutForm(DEFAULT_LAYOUT); renderLabelPreview(); });
$('label-form').addEventListener('submit', async e => {
  e.preventDefault();
  try {
    state.layout = await api('/settings/label', { method: 'PUT', body: readLayoutForm() });
    setMsg($('lb-msg'), 'บันทึกแล้ว', true);
  } catch (err) {
    setMsg($('lb-msg'), err.message);
  }
});

// ── Init ──
(async function init() {
  const today = todayISO();
  $('pk-date').value = today;
  $('jf-from').value = addDaysISO(today, -7);
  $('jf-to').value = today;
  $('rp-from').value = today.slice(0, 8) + '01';
  $('rp-to').value = today;
  try {
    state.layout = await api('/settings/label');
    fillLayoutForm(state.layout);
    await Promise.all([loadStaff(), loadDrugs()]);
    await loadToday();
  } catch (err) {
    document.querySelector('main').insertAdjacentHTML('afterbegin',
      '<p class="card msg err">เชื่อมต่อเซิร์ฟเวอร์ไม่ได้: ' + esc(err.message) + '</p>');
  }
})();
