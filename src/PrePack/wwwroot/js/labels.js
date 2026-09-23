// Sticker rendering, shared by the preview, the settings preview and the print area.
// All sizes are millimetres; keep in sync with Domain/LabelLayout.cs.
//
// Each sticker:
//   ยา: Paracetamol 500 mg #30     <- left-aligned; name truncates with …, #qty always visible, no unit
//   Lot: A12345 ผู้บรรจุ:สมชาย      <- lot always visible, then "ผู้บรรจุ:" + name (truncates with …)
//   Mfd: 23/09/69
//   Exp: 22/09/70

// Frame = header strip (torn off) + rows of stickers + footer strip (torn off).
// Rows/columns are equal unless rowHeights / colWidths give one size per row / column.
function footerHeight(l) { return l.footerHeight || 0; }
function rowSpace(l) { return l.frameHeight - l.headerHeight - footerHeight(l) - l.gapY * (l.rows - 1); }
function colSpace(l) { return l.frameWidth - l.gapX * (l.cols - 1); }
function cellWidth(l) { return colSpace(l) / l.cols; }
function cellHeight(l) { return rowSpace(l) / l.rows; }

function rowHeightsOf(l) {
  const h = l.rowHeights || [];
  return h.length === l.rows ? h : Array(l.rows).fill(cellHeight(l));
}
function colWidthsOf(l) {
  const w = l.colWidths || [];
  return w.length === l.cols ? w : Array(l.cols).fill(cellWidth(l));
}
const sumOf = a => a.reduce((s, v) => s + v, 0);

// label: {drugName, qty, lotNo, packer, packDate, labelExp}
function labelCellHTML(label, layout) {
  const by = layout.buddhistYear;
  return '<div class="lb-line lb-l1"><span class="lb-trunc">ยา: ' + esc(label.drugName) + '</span>' +
      '<span class="lb-keep">#' + esc(fmtNum(label.qty)) + '</span></div>' +
    '<div class="lb-line lb-l2"><span class="lb-keep">Lot: ' + esc(label.lotNo) + '</span>' +
      '<span class="lb-trunc lb-by">ผู้บรรจุ:' + esc(label.packer) + '</span></div>' +
    '<div>Mfd: ' + fmtShortDate(label.packDate, by) + '</div>' +
    '<div>Exp: ' + fmtShortDate(label.labelExp, by) + '</div>';
}

// One frame per printed sheet; `count` stickers fill frames left-to-right, top-to-bottom.
// outline = draw cell borders and the torn-off header/footer (for alignment tests and previews).
function buildFrames(label, layout, count, outline, maxFrames) {
  const perFrame = layout.rows * layout.cols;
  const hs = rowHeightsOf(layout), ws = colWidthsOf(layout);
  const tops = hs.map((_, r) => layout.headerHeight + sumOf(hs.slice(0, r)) + r * layout.gapY);
  const lefts = ws.map((_, c) => sumOf(ws.slice(0, c)) + c * layout.gapX);
  const foot = footerHeight(layout);
  let frames = Math.max(1, Math.ceil(count / perFrame));
  if (maxFrames) frames = Math.min(frames, maxFrames);
  const cell = labelCellHTML(label, layout);
  let out = '';
  for (let f = 0; f < frames; f++) {
    out += '<div class="lb-frame' + (outline ? ' outline' : '') + '" style="width:' + layout.frameWidth +
      'mm;height:' + layout.frameHeight + 'mm">';
    out += '<div class="lb-inner" style="left:' + layout.offsetX + 'mm;top:' + layout.offsetY + 'mm">';
    if (outline && layout.headerHeight > 0) {
      out += '<div class="lb-header" style="top:0;width:' + layout.frameWidth + 'mm;height:' +
        layout.headerHeight + 'mm">ส่วนหัว (ฉีกทิ้ง)</div>';
    }
    if (outline && foot > 0) {
      out += '<div class="lb-header" style="top:' + (layout.frameHeight - foot) + 'mm;width:' + layout.frameWidth +
        'mm;height:' + foot + 'mm">ส่วนท้าย (ฉีกทิ้ง)</div>';
    }
    for (let i = 0; i < perFrame; i++) {
      if (f * perFrame + i >= count) break;
      const r = Math.floor(i / layout.cols), c = i % layout.cols;
      out += '<div class="lb-cell" style="left:' + lefts[c] + 'mm;top:' + tops[r] + 'mm;width:' + ws[c] + 'mm;height:' + hs[r] +
        'mm;padding:' + layout.padding + 'mm;font-size:' + layout.fontSize + 'pt">' + cell + '</div>';
    }
    out += '</div></div>';
  }
  return out;
}

// Mirrors LabelLayout.Validate() so the settings form can warn before saving.
function validateLayout(l) {
  if (!(l.frameWidth >= 10 && l.frameWidth <= 300 && l.frameHeight >= 10 && l.frameHeight <= 300)) return 'ขนาดแผ่นต้องอยู่ระหว่าง 10-300 มม.';
  if (!(l.rows >= 1 && l.rows <= 10 && l.cols >= 1 && l.cols <= 10)) return 'จำนวนแถว/คอลัมน์ต้องอยู่ระหว่าง 1-10';
  if (!(l.headerHeight >= 0 && footerHeight(l) >= 0)) return 'ส่วนหัว/ส่วนท้ายต้องไม่ติดลบ';
  if (l.headerHeight + footerHeight(l) >= l.frameHeight) return 'ส่วนหัว + ส่วนท้ายต้องเล็กกว่าความสูงแผ่น';
  if (l.gapX < 0 || l.gapY < 0 || l.padding < 0) return 'ระยะห่างต้องไม่ติดลบ';
  if (Math.abs(l.offsetX) > 20 || Math.abs(l.offsetY) > 20) return 'การเลื่อนตำแหน่งต้องไม่เกิน ±20 มม.';
  if (!(l.fontSize >= 3 && l.fontSize <= 20)) return 'ขนาดตัวอักษรต้องอยู่ระหว่าง 3-20 pt';
  const TOL = 0.05;
  const hs = l.rowHeights || [], ws = l.colWidths || [];
  if (hs.length) {
    if (hs.length !== l.rows) return 'ใส่ความสูงให้ครบ ' + l.rows + ' แถว (หรือเว้นว่างเพื่อแบ่งเท่ากัน)';
    if (hs.some(h => !(h > 0))) return 'ความสูงแต่ละแถวต้องมากกว่า 0';
    if (sumOf(hs) > rowSpace(l) + TOL)
      return 'ความสูงแถวรวม ' + fmtNum(sumOf(hs)) + ' มม. เกินที่ว่าง ' + fmtNum(rowSpace(l)) + ' มม. (แผ่น − หัว − ท้าย − ช่องว่าง)';
  } else if (cellHeight(l) <= 0) return 'ส่วนหัว/ส่วนท้าย/ช่องว่างมากเกินไป ดวงฉลากมีความสูงติดลบ';
  if (ws.length) {
    if (ws.length !== l.cols) return 'ใส่ความกว้างให้ครบ ' + l.cols + ' คอลัมน์ (หรือเว้นว่างเพื่อแบ่งเท่ากัน)';
    if (ws.some(w => !(w > 0))) return 'ความกว้างแต่ละคอลัมน์ต้องมากกว่า 0';
    if (sumOf(ws) > colSpace(l) + TOL)
      return 'ความกว้างคอลัมน์รวม ' + fmtNum(sumOf(ws)) + ' มม. เกินที่ว่าง ' + fmtNum(colSpace(l)) + ' มม. (แผ่น − ช่องว่าง)';
  } else if (cellWidth(l) <= 0) return 'ระยะห่างมากเกินไป ดวงฉลากมีความกว้างติดลบ';
  return null;
}
