// Shared helpers. Plain JS, no build step, no network.

const $ = id => document.getElementById(id);

// Calls a C# bridge method. Every method returns JSON {ok, data} or {ok:false, error}.
async function call(method, ...args) {
  const bridge = await window.chrome.webview.hostObjects.bridge;
  const raw = await bridge[method](...args);
  const res = JSON.parse(raw);
  if (!res.ok) throw new Error(res.error || 'เกิดข้อผิดพลาด');
  return res.data;
}

function esc(s) {
  return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  })[c]);
}

function fmtNum(n) {
  const v = Number(n);
  if (!isFinite(v)) return '';
  return v.toLocaleString('en-US', { maximumFractionDigits: 2 });
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

function addDaysISO(iso, days) {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

function firstOfMonthISO(iso) { return iso.slice(0, 8) + '01'; }

// Monday of the week containing iso
function weekStartISO(iso) {
  const [y, m, d] = iso.split('-').map(Number);
  const dow = new Date(Date.UTC(y, m - 1, d)).getUTCDay(); // 0 = Sunday
  return addDaysISO(iso, -((dow + 6) % 7));
}

// Label expiry = min(packDate + shelfDays, source expiry). Mirrors Domain/Expiry.cs.
function labelExpiry(packDate, shelfDays, srcExp) {
  if (!(shelfDays > 0)) return { error: 'ยังไม่รู้ประเภทยา' };
  if (srcExp && srcExp <= packDate) return { error: 'ยาในภาชนะเดิมหมดอายุแล้ว ห้ามแบ่งบรรจุ' };
  let exp = addDaysISO(packDate, shelfDays);
  const capped = !!(srcExp && srcExp < exp);
  if (capped) exp = srcExp;
  return { date: exp, capped };
}

function debounce(fn, ms) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}

function setMsg(el, text, kind) {
  el.textContent = text || '';
  el.className = el.className.replace(/\s*\b(ok|err)\b/g, '') + (text && kind ? ' ' + kind : '');
}

function csvLine(values) {
  return values.map(v => '"' + String(v == null ? '' : v).replace(/"/g, '""') + '"').join(',');
}
