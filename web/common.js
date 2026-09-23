// Shared helpers: API calls, formatting, label rendering. Plain JS, no build step.

async function api(path, opts = {}) {
  const init = { method: opts.method || 'GET', headers: {} };
  if (opts.body !== undefined) {
    init.headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(opts.body);
  }
  const res = await fetch('/api' + path, init);
  let data = null;
  try { data = await res.json(); } catch (e) { data = null; }
  if (!res.ok) throw new Error((data && data.error) || ('HTTP ' + res.status));
  return data;
}

function esc(s) {
  return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  })[c]);
}

function fmtNum(n) {
  const v = Number(n);
  return Number.isInteger(v) ? String(v) : v.toFixed(2).replace(/\.?0+$/, '');
}

// "2026-09-23" -> "23/09/69" (BE) or "23/09/26" (CE)
function fmtShortDate(iso, buddhist) {
  if (!iso) return '';
  const [y, m, d] = iso.split('-');
  const yy = buddhist ? Number(y) + 543 : Number(y);
  return d + '/' + m + '/' + String(yy).slice(-2);
}

// "2026-09-23" -> "23/09/2569"
function fmtThaiDate(iso) {
  if (!iso) return '';
  const [y, m, d] = iso.split('-');
  return d + '/' + m + '/' + (Number(y) + 543);
}

function todayISO() {
  const t = new Date();
  const p = n => String(n).padStart(2, '0');
  return t.getFullYear() + '-' + p(t.getMonth() + 1) + '-' + p(t.getDate());
}

function addDaysISO(iso, days) {
  const [y, m, d] = iso.split('-').map(Number);
  const t = new Date(Date.UTC(y, m - 1, d + days));
  return t.toISOString().slice(0, 10);
}

const PACK_TYPE_LABEL = { prepack: 'Pre-pack', unitdose: 'Unit dose', cream: 'แบ่งครีม' };
const STATUS_LABEL = { packed: 'รอตรวจ', checked: 'ตรวจแล้ว', cancelled: 'ยกเลิก' };

// ── Label rendering (shared by settings preview and print page) ──

function labelCellHTML(job, layout) {
  const name = (job.drug_name + ' ' + (job.strength || '')).trim();
  const qty = '#' + fmtNum(job.qty_per_pack) + ' ' + job.unit;
  let html = '<div class="lb-name">' + esc(name) + '</div>' +
    '<div class="lb-row"><span>' + esc(qty) + '</span><span>L:' + esc(job.lot_no) + '</span></div>' +
    '<div>บรรจุ ' + fmtShortDate(job.pack_date, layout.buddhist_year) + '</div>' +
    '<div class="lb-exp">EXP ' + fmtShortDate(job.bud_date, layout.buddhist_year) + '</div>';
  if (layout.show_packer) html += '<div>ผู้บรรจุ ' + esc(job.packer_short) + '</div>';
  return html;
}

function cellWidth(l) { return (l.frame_width - l.gap_x * (l.cols - 1)) / l.cols; }
function cellHeight(l) { return (l.frame_height - l.header_height - l.gap_y * (l.rows - 1)) / l.rows; }

// Build frames (one per printed sheet) for `count` labels, skipping the first
// `skip` cells of the first frame. Returns an HTML string.
function buildFrames(job, layout, count, skip, outline) {
  const perFrame = layout.rows * layout.cols;
  const cw = cellWidth(layout), ch = cellHeight(layout);
  const total = skip + count;
  const frames = Math.max(1, Math.ceil(total / perFrame));
  const cell = labelCellHTML(job, layout);
  let out = '';
  for (let f = 0; f < frames; f++) {
    out += '<div class="lb-frame' + (outline ? ' outline' : '') + '" style="width:' + layout.frame_width +
      'mm;height:' + layout.frame_height + 'mm">';
    out += '<div class="lb-inner" style="left:' + layout.offset_x + 'mm;top:' + layout.offset_y + 'mm">';
    if (outline) {
      out += '<div class="lb-header" style="width:' + layout.frame_width + 'mm;height:' +
        layout.header_height + 'mm">ส่วนหัว (ทิ้ง)</div>';
    }
    for (let i = 0; i < perFrame; i++) {
      const n = f * perFrame + i;
      if (n < skip || n >= total) continue;
      const r = Math.floor(i / layout.cols), c = i % layout.cols;
      const x = c * (cw + layout.gap_x);
      const y = layout.header_height + r * (ch + layout.gap_y);
      out += '<div class="lb-cell" style="left:' + x + 'mm;top:' + y + 'mm;width:' + cw + 'mm;height:' + ch +
        'mm;padding:' + layout.padding + 'mm;font-size:' + layout.font_size + 'pt">' + cell + '</div>';
    }
    out += '</div></div>';
  }
  return out;
}
