// Development only: a fake C# bridge so the UI can be opened in a normal browser.
// index.html loads this file only when window.chrome.webview is missing (i.e. not inside PrePack.exe).
// The settings password in this mock is 'dev' (the real one is not in the repo).
(function () {
  const today = new Date().toISOString().slice(0, 10);
  const add = (d, n) => new Date(Date.parse(d) + n * 864e5).toISOString().slice(0, 10);
  const config = {
    layout: { frameWidth: 85, frameHeight: 50, headerHeight: 8, rows: 3, cols: 3, gapX: 1, gapY: 1, offsetX: 0, offsetY: 0, padding: 0.8, fontSize: 5.5, buddhistYear: true },
    drugTypes: [
      { key: 'tablet', label: 'ยาเม็ด', unit: 'เม็ด', shelfDays: 365, qtyPresets: [30, 60, 100, 1, 2] },
      { key: 'cream', label: 'ครีม', unit: 'g', shelfDays: 180, qtyPresets: [5, 10] },
      { key: 'liquid', label: 'ยาน้ำ', unit: 'ml', shelfDays: 30, qtyPresets: [30, 60] },
    ],
    unitMap: { TAB: 'tablet', CAP: 'tablet', TUBE: 'cream', BOT: 'liquid' },
    nameKeywords: { tablet: ['tab', 'cap'], cream: ['cream', 'oint', 'gel'], liquid: ['syr', 'susp', 'sol'] },
    printerName: 'Mock Sticker Printer', lastStaffUid: 'u1',
    mysql: { host: '', port: 3306, user: '', database: 'prepack', hasPassword: false },
    invs: { host: 'mock', port: 1433, database: 'INVS', user: 'sa', iniPath: 'C:\\INVS\\invs.ini', unitColumn: 'DISP_UNIT', hasPassword: true },
  };
  let staff = [{ uid: 'u1', name: 'สมชาย ใจดี', shortName: 'สมชาย', active: true }, { uid: 'u2', name: 'สุดา รักงาน', shortName: 'สุดา', active: true }];
  let workFactors = [{ drugType: 'tablet', maxQty: 5, factor: 2 }, { drugType: 'tablet', maxQty: 30, factor: 1 }, { drugType: 'tablet', maxQty: null, factor: 1.5 }, { drugType: 'cream', maxQty: null, factor: 1 }, { drugType: 'liquid', maxQty: null, factor: 1 }];
  const labels = { 'Amoxicillin/Clavulanate 1 g (Augmentin) tab': 'Augmentin 1 g' };
  let sync = { configured: false, running: false, lastSyncAt: null, lastError: null, unsynced: 3, inBackoff: false };
  const drugs = [
    { workingCode: '1000123', drugName: 'Paracetamol 500 mg tab', drugNameTh: 'พาราเซตามอล', invsUnit: 'TAB', drugType: 'tablet' },
    { workingCode: '1000456', drugName: 'Amoxicillin/Clavulanate 1 g (Augmentin) tab', drugNameTh: '', invsUnit: 'TAB', drugType: 'tablet' },
    { workingCode: '2000789', drugName: 'Triamcinolone 0.1% cream', drugNameTh: '', invsUnit: 'JAR', drugType: 'cream' },
    { workingCode: '3000111', drugName: 'Ferrous fumarate syrup', drugNameTh: '', invsUnit: 'BOT', drugType: 'liquid' },
  ];
  const workload = [
    { staffUid: 'u1', name: 'สมชาย ใจดี', items: 98, pages: 340, stickers: 3060, tablet: 2610, cream: 180, liquid: 270, manual: 6, points: 4210 },
    { staffUid: 'u2', name: 'สุดา รักงาน', items: 84, pages: 290, stickers: 2610, tablet: 2250, cream: 90, liquid: 270, manual: 7, points: 2890.5 },
  ];
  const logs = [{ printedAt: today + 'T09:12:00', staffName: 'สมชาย ใจดี', source: 'INVS', workingCode: '1000123',
    drugName: 'Paracetamol 500 mg tab', drugType: 'tablet', unit: 'เม็ด', qtyPerPack: 30, lotNo: 'A12345', packDate: today,
    srcExpDate: add(today, 500), labelExpDate: add(today, 365), pages: 2, stickers: 18, workFactor: 1.5, workPoints: 27,
    machineName: 'PHARM-01', synced: false }];
  const origin = () => sync.configured ? { origin: 'mysql', sync } : { origin: 'local', reason: 'ยังไม่ได้ตั้งค่า MySQL', sync };
  const err = e => JSON.stringify({ ok: false, error: e });

  const ok = data => JSON.stringify({ ok: true, data });
  const api = {
    Init: () => ok({ today, version: '1.0.0-dev', machine: 'BROWSER', config, staff, workFactors, sync }),
    SyncStatus: () => ok(sync),
    SyncNow: () => sync.configured ? (sync = Object.assign({}, sync, { unsynced: 0, lastSyncAt: today + 'T10:30:00' }), ok({ sync, staff, workFactors })) : err('ยังไม่ได้ตั้งค่า MySQL — ข้อมูลเก็บในเครื่องนี้อยู่แล้ว'),
    ResyncAll: () => ok({ queued: 3, sync }),
    SearchDrugs: q => ok(drugs.filter(d => (d.drugName + d.workingCode).toLowerCase().includes(q.toLowerCase()))),
    GetLots: () => ok([{ lotNo: 'A12345', expiryDate: add(today, 200), qty: 1000 }, { lotNo: 'B67890', expiryDate: add(today, 700), qty: 3000 }]),
    ClassifyName: n => ok(/cream|oint|gel/i.test(n) ? 'cream' : /syr|susp/i.test(n) ? 'liquid' : /tab|cap/i.test(n) ? 'tablet' : ''),
    FrequentQty: () => ok([60]),
    SetLastStaff: () => ok(true),
    Print: json => { const j = JSON.parse(json); console.log('[mock] Print', j); sync = Object.assign({}, sync, { unsynced: sync.unsynced + (j.test ? 0 : 1) }); return ok({ logged: !j.test, online: sync.configured, points: (j.stickers || 0) * 1.5, sync }); },
    ListStaff: () => ok(staff),
    SaveStaff: json => { const s = JSON.parse(json); if (s.uid) staff = staff.map(x => x.uid === s.uid ? Object.assign({}, x, s) : x); else staff.push(Object.assign({}, s, { uid: 'u' + (staff.length + 1), active: true })); return ok({ staff, online: sync.configured, sync }); },
    Snapshot: () => ok({ staff, workFactors, sync }),
    GetUiZoom: () => ok(config.uiZoomPercent || 100),
    SetUiZoom: v => { config.uiZoomPercent = v; document.body.style.zoom = v / 100; return ok(v); },
    GetDrugLabel: json => ok({ labelName: labels[JSON.parse(json).drugName] || '' }),
    SaveDrugLabel: json => { const j = JSON.parse(json); const l = j.labelName.trim() === j.drugName ? '' : j.labelName.trim(); labels[j.drugName] = l; return ok({ labelName: l, online: sync.configured, sync }); },
    GetPrinters: () => ok(['Mock Sticker Printer', 'Microsoft Print to PDF']),
    SaveLabelSettings: json => { const j = JSON.parse(json); config.layout = j.layout; config.printerName = j.printerName; return ok(config); },
    SaveDrugTypes: json => { Object.assign(config, JSON.parse(json)); return ok(config); },
    ResetDrugTypes: () => ok(config),
    SaveMySql: json => { Object.assign(config.mysql, JSON.parse(json), { hasPassword: true }); sync = Object.assign({}, sync, { configured: true, unsynced: 0, lastSyncAt: today + 'T10:31:00' }); return ok({ applied: ['001_init', '002_staff_uid'], config, sync, staff, workFactors }); },
    SaveInvs: () => ok({ config, unitColumns: ['DISP_UNIT', 'DOSAGE_FORM'] }),
    FindInvsIni: () => ok({ config }),
    BrowseInvsIni: () => ok({ cancelled: true }),
    GetWorkFactors: () => ok(workFactors),
    CheckAdminPassword: pw => pw.trim() === 'dev' ? ok(true) : err('รหัสไม่ถูกต้อง'),
    SaveWorkFactors: (json, pw) => { if (pw !== 'dev') return err('รหัสไม่ถูกต้อง'); workFactors = JSON.parse(json); return ok(workFactors); },
    Workload: () => ok(Object.assign({ rows: workload }, origin())),
    Logs: () => ok(Object.assign({ rows: logs }, origin())),
    SaveCsv: (name, content) => { console.log('[mock] SaveCsv', name, content); return ok({ saved: true }); },
  };
  const bridge = new Proxy({}, { get: (_, m) => m === 'then' ? undefined : (...a) => Promise.resolve(api[m] ? api[m](...a) : err('mock: ' + String(m))) });
  window.chrome = window.chrome || {};
  window.chrome.webview = { hostObjects: { bridge: Promise.resolve(bridge) } };

  // #custom → a layout with header + footer and per-row / per-column sizes (for screenshots).
  if (/custom/.test(location.hash)) Object.assign(config.layout, { headerHeight: 6, footerHeight: 4,
    rowHeights: [12, 13, 13], colWidths: [30, 25, 28] });

  // index.html#open=report → open that ⚙ tab after start-up (for screenshots).
  const m = /#open=(\w+)/.exec(location.hash);
  if (m) window.addEventListener('load', () => setTimeout(() => openSettings(m[1]), 300));
  // index.html#demo → pick a drug and a quantity so the preview shows stickers.
  if (/#demo/.test(location.hash)) window.addEventListener('load', () => setTimeout(async () => {
    await chooseInvs(drugs[1]);
    $('qty').value = 14; markChip(); render();
  }, 300));
})();
