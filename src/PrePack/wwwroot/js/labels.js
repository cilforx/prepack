// Sticker rendering, shared by the preview, the settings preview and the print area.
// All sizes are millimetres; keep in sync with Domain/LabelLayout.cs.
//
// Each sticker (all lines left-aligned):
//   ยา: Paracetamol 500 mg #30     <- name truncates with …, #qty always visible, no unit
//   Lot: A12345 สมชาย              <- lot always visible, packer name truncates
//   Mfd: 23/09/69
//   Exp: 22/09/70

function cellWidth(l) { return (l.frameWidth - l.gapX * (l.cols - 1)) / l.cols; }
function cellHeight(l) { return (l.frameHeight - l.headerHeight - l.gapY * (l.rows - 1)) / l.rows; }

// label: {drugName, qty, lotNo, packer, packDate, labelExp}
function labelCellHTML(label, layout) {
  const by = layout.buddhistYear;
  return '<div class="lb-line lb-l1"><span class="lb-trunc">ยา: ' + esc(label.drugName) + '</span>' +
      '<span class="lb-keep">#' + esc(fmtNum(label.qty)) + '</span></div>' +
    '<div class="lb-line"><span class="lb-keep">Lot: ' + esc(label.lotNo) + '</span>' +
      '<span class="lb-trunc">' + esc(label.packer) + '</span></div>' +
    '<div>Mfd: ' + fmtShortDate(label.packDate, by) + '</div>' +
    '<div>Exp: ' + fmtShortDate(label.labelExp, by) + '</div>';
}

// One frame per printed sheet; `count` stickers fill frames left-to-right, top-to-bottom.
// outline = draw cell borders and the torn-off header (for alignment tests and previews).
function buildFrames(label, layout, count, outline, maxFrames) {
  const perFrame = layout.rows * layout.cols;
  const cw = cellWidth(layout), ch = cellHeight(layout);
  let frames = Math.max(1, Math.ceil(count / perFrame));
  if (maxFrames) frames = Math.min(frames, maxFrames);
  const cell = labelCellHTML(label, layout);
  let out = '';
  for (let f = 0; f < frames; f++) {
    out += '<div class="lb-frame' + (outline ? ' outline' : '') + '" style="width:' + layout.frameWidth +
      'mm;height:' + layout.frameHeight + 'mm">';
    out += '<div class="lb-inner" style="left:' + layout.offsetX + 'mm;top:' + layout.offsetY + 'mm">';
    if (outline) {
      out += '<div class="lb-header" style="width:' + layout.frameWidth + 'mm;height:' +
        layout.headerHeight + 'mm">ส่วนหัว (ฉีกทิ้ง)</div>';
    }
    for (let i = 0; i < perFrame; i++) {
      if (f * perFrame + i >= count) break;
      const r = Math.floor(i / layout.cols), c = i % layout.cols;
      const x = c * (cw + layout.gapX);
      const y = layout.headerHeight + r * (ch + layout.gapY);
      out += '<div class="lb-cell" style="left:' + x + 'mm;top:' + y + 'mm;width:' + cw + 'mm;height:' + ch +
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
  if (!(l.headerHeight >= 0 && l.headerHeight < l.frameHeight)) return 'ส่วนหัวต้องเล็กกว่าความสูงแผ่น';
  if (l.gapX < 0 || l.gapY < 0 || l.padding < 0) return 'ระยะห่างต้องไม่ติดลบ';
  if (Math.abs(l.offsetX) > 20 || Math.abs(l.offsetY) > 20) return 'การเลื่อนตำแหน่งต้องไม่เกิน ±20 มม.';
  if (!(l.fontSize >= 3 && l.fontSize <= 20)) return 'ขนาดตัวอักษรต้องอยู่ระหว่าง 3-20 pt';
  if (cellWidth(l) <= 0 || cellHeight(l) <= 0) return 'ระยะห่างมากเกินไป ดวงฉลากมีขนาดติดลบ';
  return null;
}
