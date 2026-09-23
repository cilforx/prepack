// Development only: a fake C# bridge so the UI can be opened in a normal browser.
// index.html loads this file only when window.chrome.webview is missing (i.e. not inside PrePack.exe).
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
    printerName: 'Mock Sticker Printer', lastStaffId: 1,
    mysql: { host: 'mock', port: 3306, user: 'prepack', database: 'prepack', hasPassword: true },
    invs: { host: 'mock', port: 1433, database: 'INVS', user: 'sa', iniPath: 'C:\\INVS\\invs.ini', unitColumn: 'DISP_UNIT', hasPassword: true },
  };
  let staff = [{ id: 1, name: 'สมชาย ใจดี', shortName: 'สมชาย', active: true }, { id: 2, name: 'สุดา รักงาน', shortName: 'สุดา', active: true }];
  const drugs = [
    { workingCode: '1000123', drugName: 'Paracetamol 500 mg tab', drugNameTh: 'พาราเซตามอล', invsUnit: 'TAB', drugType: 'tablet' },
    { workingCode: '1000456', drugName: 'Amoxicillin/Clavulanate 1 g (Augmentin) tab', drugNameTh: '', invsUnit: 'TAB', drugType: 'tablet' },
    { workingCode: '2000789', drugName: 'Triamcinolone 0.1% cream', drugNameTh: '', invsUnit: 'JAR', drugType: 'cream' },
    { workingCode: '3000111', drugName: 'Ferrous fumarate syrup', drugNameTh: '', invsUnit: 'BOT', drugType: 'liquid' },
  ];
  const workload = [
    { staffId: 1, name: 'สมชาย ใจดี', items: 98, pages: 340, stickers: 3060, tablet: 2610, cream: 180, liquid: 270, manual: 6, points: 4210 },
    { staffId: 2, name: 'สุดา รักงาน', items: 84, pages: 290, stickers: 2610, tablet: 2250, cream: 90, liquid: 270, manual: 7, points: 2890.5 },
  ];
  const logs = [{ id: 1, printedAt: today + 'T09:12:00', staffName: 'สมชาย ใจดี', source: 'INVS', workingCode: '1000123',
    drugName: 'Paracetamol 500 mg tab', drugType: 'tablet', unit: 'เม็ด', qtyPerPack: 30, lotNo: 'A12345', packDate: today,
    srcExpDate: add(today, 500), labelExpDate: add(today, 365), pages: 2, stickers: 18, workFactor: 1.5, workPoints: 27, machineName: 'PHARM-01' }];

  let workFactors = [{ drugType: 'tablet', maxQty: 5, factor: 2 }, { drugType: 'tablet', maxQty: 30, factor: 1 }, { drugType: 'tablet', maxQty: null, factor: 1.5 }, { drugType: 'cream', maxQty: null, factor: 1 }, { drugType: 'liquid', maxQty: null, factor: 1 }];
  // mock only: the settings password here is 'dev' (the real one is not in the repo)
  const ok = data => JSON.stringify({ ok: true, data });
  const api = {
    Init: () => ok({ today, version: '1.0.0-dev', machine: 'BROWSER', config, staff, workFactors, dbReady: true, dbError: null, pendingLogs: 0 }),
    SearchDrugs: q => ok(drugs.filter(d => (d.drugName + d.workingCode).toLowerCase().includes(q.toLowerCase()))),
    GetLots: () => ok([{ lotNo: 'A12345', expiryDate: add(today, 200), qty: 1000 }, { lotNo: 'B67890', expiryDate: add(today, 700), qty: 3000 }]),
    ClassifyName: n => ok(/cream|oint|gel/i.test(n) ? 'cream' : /syr|susp/i.test(n) ? 'liquid' : /tab|cap/i.test(n) ? 'tablet' : ''),
    FrequentQty: () => ok([60]),
    SetLastStaff: () => ok(true),
    Print: json => { console.log('[mock] Print', JSON.parse(json), document.getElementById('print-area').innerHTML.length); return ok({ logged: true, queued: false, pendingLogs: 0 }); },
    ListStaff: () => ok(staff),
    SaveStaff: json => { const s = JSON.parse(json); if (s.id) staff = staff.map(x => x.id === s.id ? Object.assign({}, x, s) : x); else staff.push(Object.assign({}, s, { id: staff.length + 1, active: true })); return ok(staff); },
    GetPrinters: () => ok(['Mock Sticker Printer', 'Microsoft Print to PDF']),
    SaveLabelSettings: json => { const j = JSON.parse(json); config.layout = j.layout; config.printerName = j.printerName; return ok(config); },
    SaveDrugTypes: json => { Object.assign(config, JSON.parse(json)); return ok(config); },
    ResetDrugTypes: () => ok(config),
    SaveMySql: () => ok({ applied: [], config, staff }),
    SaveInvs: () => ok({ config, unitColumns: ['DISP_UNIT', 'DOSAGE_FORM'] }),
    FindInvsIni: () => ok({ config }),
    BrowseInvsIni: () => ok({ cancelled: true }),
    GetWorkFactors: () => ok(workFactors),
    CheckAdminPassword: pw => pw === 'dev' ? ok(true) : JSON.stringify({ ok: false, error: 'รหัสไม่ถูกต้อง' }),
    SaveWorkFactors: (json, pw) => { if (pw !== 'dev') return JSON.stringify({ ok: false, error: 'รหัสไม่ถูกต้อง' }); workFactors = JSON.parse(json); return ok(workFactors); },
    Workload: () => ok(workload),
    Logs: () => ok(logs),
    SaveCsv: (name, content) => { console.log('[mock] SaveCsv', name, content); return ok({ saved: true }); },
  };
  const bridge = new Proxy({}, { get: (_, m) => m === 'then' ? undefined : (...a) => Promise.resolve(api[m] ? api[m](...a) : JSON.stringify({ ok: false, error: 'mock: ' + String(m) })) });
  window.chrome = window.chrome || {};
  window.chrome.webview = { hostObjects: { bridge: Promise.resolve(bridge) } };
})();
