// Label print page: print.html?job=ID[&copies=N][&start=K]
(async function () {
  const qs = new URLSearchParams(location.search);
  const jobId = qs.get('job');
  const info = document.getElementById('job-info');
  const copiesEl = document.getElementById('copies');
  const startEl = document.getElementById('start');
  const outlineEl = document.getElementById('outline');
  const sheets = document.getElementById('sheets');

  let job, layout;
  try {
    [job, layout] = await Promise.all([api('/jobs/' + encodeURIComponent(jobId)), api('/settings/label')]);
  } catch (e) {
    info.textContent = 'โหลดข้อมูลไม่ได้: ' + e.message;
    return;
  }

  info.innerHTML = '<b>' + esc(job.drug_name + ' ' + job.strength) + '</b> #' + fmtNum(job.qty_per_pack) + ' ' +
    esc(job.unit) + ' × ' + job.pack_count + ' ซอง · Lot ' + esc(job.lot_no) +
    (job.status === 'cancelled' ? ' <span class="warn">(รายการนี้ถูกยกเลิก)</span>' : '');
  copiesEl.value = qs.get('copies') || job.pack_count;
  startEl.value = qs.get('start') || 1;
  startEl.max = layout.rows * layout.cols;

  document.getElementById('page-size').textContent =
    '@page { size: ' + layout.frame_width + 'mm ' + layout.frame_height + 'mm; margin: 0; }';

  function render() {
    const count = Math.max(1, Math.min(2000, parseInt(copiesEl.value, 10) || 1));
    const skip = Math.max(0, Math.min(layout.rows * layout.cols - 1, (parseInt(startEl.value, 10) || 1) - 1));
    sheets.innerHTML = buildFrames(job, layout, count, skip, outlineEl.checked);
  }
  copiesEl.addEventListener('input', render);
  startEl.addEventListener('input', render);
  outlineEl.addEventListener('change', render);
  document.getElementById('btn-print').addEventListener('click', () => window.print());
  render();
})();
