const now = new Date();
const ago = (minutes) => new Date(now.getTime() - minutes * 60000);
const fmt = (date) => new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit' }).format(date);
const dateFmt = (date) => new Intl.DateTimeFormat('pl-PL', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).format(date);
const rooms = [
  { id: 'prac-101', name: 'Pracownia 101', count: 6, label: 'Sala 101' },
  { id: 'prac-102', name: 'Pracownia 102', count: 6, label: 'Sala 102' },
  { id: 'prac-205', name: 'Pracownia 205', count: 4, label: 'Sala 205' }
];
const devices = [
  ['KS-101-01','prac-101','Online','10.20.101.11','j.kowalski',4,'Windows 11 Pro'],
  ['KS-101-02','prac-101','Online','10.20.101.12','a.nowak',12,'Windows 11 Pro'],
  ['KS-101-03','prac-101','Online','10.20.101.13','m.wisniewski',27,'Windows 10 Pro'],
  ['KS-101-04','prac-101','Online','10.20.101.14','p.zielinski',39,'Windows 11 Pro'],
  ['KS-101-05','prac-101','Offline','10.20.101.15','—',190,'Windows 10 Pro'],
  ['KS-101-06','prac-101','Brak danych','—','—',null,'Windows 10 Pro'],
  ['KS-102-01','prac-102','Online','10.20.102.11','k.mazur',8,'Windows 11 Pro'],
  ['KS-102-02','prac-102','Online','10.20.102.12','t.lewandowski',18,'Windows 11 Pro'],
  ['KS-102-03','prac-102','Online','10.20.102.13','j.kowalski',32,'Windows 10 Pro'],
  ['KS-102-04','prac-102','Online','10.20.102.14','a.nowak',45,'Windows 11 Pro'],
  ['KS-102-05','prac-102','Offline','10.20.102.15','—',250,'Windows 10 Pro'],
  ['KS-102-06','prac-102','Brak danych','—','—',null,'Windows 10 Pro'],
  ['KS-205-01','prac-205','Online','10.20.205.11','e.dabrowska',14,'Windows 11 Pro'],
  ['KS-205-02','prac-205','Online','10.20.205.12','p.zielinski',29,'Windows 11 Pro'],
  ['KS-205-03','prac-205','Online','10.20.205.13','k.mazur',55,'Windows 10 Pro'],
  ['KS-205-04','prac-205','Offline','10.20.205.14','—',220,'Windows 10 Pro'],
  ['KS-SERWER-01',null,'Online','10.20.0.5','admin.it',3,'Windows Server 2019']
].map(([name, room, status, ip, user, minutes, os]) => ({ name, room, status, ip, user, minutes, os, lastSeen: minutes === null ? null : ago(minutes) }));
const people = [
  { user:'j.kowalski', name:'Jan Kowalski', role:'Uczeń · 3TI', room:'Pracownia 101', last:'Dzisiaj, 08:42', state:'Aktywne' },
  { user:'a.nowak', name:'Anna Nowak', role:'Uczeń · 3TI', room:'Pracownia 101', last:'Dzisiaj, 08:34', state:'Aktywne' },
  { user:'k.mazur', name:'Katarzyna Mazur', role:'Nauczyciel', room:'Pracownia 102', last:'Dzisiaj, 08:28', state:'Aktywne' },
  { user:'t.lewandowski', name:'Tomasz Lewandowski', role:'Nauczyciel', room:'Pracownia 102', last:'Dzisiaj, 08:19', state:'Aktywne' },
  { user:'m.wisniewski', name:'Marek Wiśniewski', role:'Uczeń · 2TI', room:'Pracownia 101', last:'Wczoraj, 14:12', state:'Aktywne' },
  { user:'p.zielinski', name:'Paweł Zieliński', role:'Uczeń · 2TI', room:'Pracownia 205', last:'Wczoraj, 13:56', state:'Aktywne' },
  { user:'e.dabrowska', name:'Ewa Dąbrowska', role:'Nauczyciel', room:'Pracownia 205', last:'Wczoraj, 12:40', state:'Aktywne' },
  { user:'admin.it', name:'Administrator IT', role:'Administrator', room:'Serwerownia', last:'Dzisiaj, 07:58', state:'Aktywne' }
];
const loginRows = [
  {when:ago(3),user:'Jan Kowalski',account:'j.kowalski',computer:'KS-101-01',room:'Pracownia 101',type:'Logowanie'},
  {when:ago(11),user:'Anna Nowak',account:'a.nowak',computer:'KS-101-02',room:'Pracownia 101',type:'Logowanie'},
  {when:ago(18),user:'Katarzyna Mazur',account:'k.mazur',computer:'KS-102-01',room:'Pracownia 102',type:'Logowanie'},
  {when:ago(24),user:'Tomasz Lewandowski',account:'t.lewandowski',computer:'KS-102-02',room:'Pracownia 102',type:'Logowanie'},
  {when:ago(39),user:'Paweł Zieliński',account:'p.zielinski',computer:'KS-205-02',room:'Pracownia 205',type:'Wylogowanie'},
  {when:ago(57),user:'Marek Wiśniewski',account:'m.wisniewski',computer:'KS-102-03',room:'Pracownia 102',type:'Nieudana próba'},
  {when:ago(73),user:'Ewa Dąbrowska',account:'e.dabrowska',computer:'KS-205-01',room:'Pracownia 205',type:'Logowanie'},
  {when:ago(96),user:'Jan Kowalski',account:'j.kowalski',computer:'KS-102-03',room:'Pracownia 102',type:'Wylogowanie'}
];
const auditRows = [
  {when:ago(16),actor:'admin.it',action:'Restart komputera',target:'KS-102-05',result:'Symulacja'},
  {when:ago(72),actor:'admin.it',action:'Podgląd konta',target:'j.kowalski',result:'Sukces'},
  {when:ago(145),actor:'k.mazur',action:'Lista komputerów',target:'Pracownia 101',result:'Sukces'},
  {when:ago(220),actor:'admin.it',action:'Blokada komputera',target:'KS-101-05',result:'Symulacja'},
  {when:ago(302),actor:'admin.it',action:'Zapytanie o logowania',target:'prac-205',result:'Sukces'}
];
const viewMeta = {
  overview:['Pulpit','Dzień dobry, Anno','Szybki przegląd stanu szkolnych pracowni.','Komputery — ostatni stan','Ostatnio sprawdzone urządzenia w całej szkole'],
  computers:['Komputery','Komputery','Inwentarz urządzeń i ostatnio zaobserwowany stan.','Komputery','Wszystkie urządzenia w szkolnej sieci'],
  logins:['Historia logowań','Historia logowań','Zdarzenia logowania z przykładowego dziennika szkolnego.','Ostatnie zdarzenia','Historia zdarzeń — nie lista aktywnych sesji'],
  users:['Konta użytkowników','Konta użytkowników','Podgląd fikcyjnych kont i ostatniej aktywności.','Użytkownicy','Konta w przykładowym katalogu szkolnym'],
  audit:['Akcje i audyt','Akcje i audyt','Przykładowy rejestr operacji i zapytań.','Ostatnie operacje','Wpisy audytu w środowisku demonstracyjnym']
};
let activeView='overview';
let selectedRoom=null;
let toastTimer;
const $=(selector)=>document.querySelector(selector);
const escapeHtml=(value)=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

function init(){
  $('#today-date').textContent=new Intl.DateTimeFormat('pl-PL',{day:'numeric',month:'long',year:'numeric'}).format(now);
  $('#weekday').textContent=new Intl.DateTimeFormat('pl-PL',{weekday:'long'}).format(now).toLocaleUpperCase('pl-PL');
  renderRooms(); renderRoomCards(); setView('overview');
  document.querySelectorAll('.nav-item').forEach(btn=>btn.addEventListener('click',()=>setView(btn.dataset.view)));
  document.querySelectorAll('[data-go]').forEach(btn=>btn.addEventListener('click',()=>setView(btn.dataset.go)));
  $('#search-input').addEventListener('input',renderTable);
  $('#status-filter').addEventListener('change',renderTable);
  $('#room-filter-button').addEventListener('click',()=>{selectedRoom=selectedRoom?null:'prac-101';updateRoomFilter();renderTable();showToast(selectedRoom?'Filtr ustawiony na Pracownię 101':'Wyświetlane są wszystkie pracownie');});
  $('#refresh-button').addEventListener('click',()=>showToast('Widok demonstracyjny jest aktualny — dane są przykładowe.'));
  $('#export-button').addEventListener('click',exportCsv);
  $('#help-button').addEventListener('click',showDemoInfo);
  $('#cancel-action').addEventListener('click',()=>$('#action-dialog').close());
  $('#confirm-action').addEventListener('click',()=>{$('#action-dialog').close();showToast('Symulacja zakończona. Żadne urządzenie ani konto nie zostało zmienione.');});
}
function renderRooms(){
  $('#room-list').innerHTML=rooms.map(r=>`<button class="room-link" data-room="${r.id}"><span class="room-glyph">⌂</span>${r.name}<span class="room-total">${r.count}</span></button>`).join('');
  document.querySelectorAll('.room-link').forEach(btn=>btn.addEventListener('click',()=>selectRoom(btn.dataset.room)));
}
function renderRoomCards(){
  $('#room-cards').innerHTML=rooms.map(r=>{
    const list=devices.filter(d=>d.room===r.id), online=list.filter(d=>d.status==='Online').length, ratio=Math.round(online/list.length*100), issue=list.filter(d=>d.status!=='Online').length;
    return `<article class="room-card" tabindex="0" role="button" data-room-card="${r.id}"><div class="room-card-top"><div><h3>${r.name}</h3><div class="room-sub">${r.count} komputerów · ${r.label}</div></div><span class="room-arrow">↗</span></div><div class="room-progress"><span style="width:${ratio}%"></span></div><div class="room-card-bottom"><span><strong>${online} / ${r.count}</strong> dostępnych</span><span class="${issue?'room-issue':''}">${issue?`${issue} do sprawdzenia`:'Wszystko w porządku'}</span></div></article>`;
  }).join('');
  document.querySelectorAll('[data-room-card]').forEach(card=>{card.addEventListener('click',()=>selectRoom(card.dataset.roomCard));card.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();selectRoom(card.dataset.roomCard);}})});
}
function setView(view){
  activeView=view;
  document.querySelectorAll('.nav-item').forEach(btn=>btn.classList.toggle('active',btn.dataset.view===view));
  const meta=viewMeta[view];$('#breadcrumb').textContent=meta[0];$('#page-title').textContent=meta[1];$('#page-description').textContent=meta[2];$('#table-title').textContent=meta[3];$('#table-description').textContent=meta[4];
  $('.room-section').style.display=view==='overview'?'block':'none';
  const heads={
    overview:['URZĄDZENIE','STAN','ADRES IP','OSTATNI UŻYTKOWNIK','OSTATNIE ZDARZENIE','SYSTEM',''],
    computers:['URZĄDZENIE','STAN','ADRES IP','PRACOWNIA','OSTATNI STAN','SYSTEM',''],
    logins:['CZAS','UŻYTKOWNIK','KOMPUTER','PRACOWNIA','ZDARZENIE',''],
    users:['UŻYTKOWNIK','ROLA / KLASA','OSTATNIA AKTYWNOŚĆ','OSTATNIA STACJA','STATUS',''],
    audit:['CZAS','WYKONAŁ','OPERACJA','CEL','WYNIK','']
  };
  $('#table-head').innerHTML=`<tr>${heads[view].map(h=>`<th>${h}</th>`).join('')}</tr>`;
  $('#search-input').placeholder=view==='users'?'Szukaj użytkownika…':view==='logins'?'Szukaj użytkownika lub komputera…':'Szukaj komputera, użytkownika…';
  renderTable();
}
function filteredDevices(){
  const query=$('#search-input').value.trim().toLocaleLowerCase('pl'), status=$('#status-filter').value;
  return devices.filter(d=>(!selectedRoom||d.room===selectedRoom)&& (status==='all'||d.status===status) && (!query||[d.name,d.user,d.ip,d.room&&rooms.find(r=>r.id===d.room)?.name].join(' ').toLocaleLowerCase('pl').includes(query)));
}
function renderTable(){
  const body=$('#table-body');let rows=[];
  if(activeView==='overview'||activeView==='computers'){
    const data=filteredDevices();const limit=activeView==='overview'?5:12;
    rows=data.slice(0,limit).map(d=>`<tr data-device="${d.name}"><td><div class="computer-cell"><span class="device-icon">▣</span><div><div class="computer-name">${d.name}</div><div class="computer-meta">${escapeHtml(d.os)}</div></div></div></td><td>${statusPill(d.status)}</td><td class="ip-cell">${d.ip||'<span class="muted">—</span>'}</td>${activeView==='overview'?`<td>${d.user==='—'?'<span class="muted">Brak danych</span>':`<div class="user-cell"><span class="tiny-avatar">${initials(d.user)}</span>${d.user}</div>`}</td><td class="last-seen">${d.lastSeen?dateFmt(d.lastSeen):'<span class="muted">Nieznany</span>'}</td>`:`<td>${d.room?rooms.find(r=>r.id===d.room).name:'<span class="muted">Poza pracownią</span>'}</td><td class="last-seen">${d.lastSeen?dateFmt(d.lastSeen):'<span class="muted">Nieznany</span>'}</td>`}<td>${escapeHtml(d.os)}</td><td><button class="row-menu" aria-label="Szczegóły">···</button></td></tr>`);
    $('#result-count').textContent=`Pokazuje ${Math.min(data.length,limit)} z ${data.length} komputerów`;
  } else if(activeView==='logins'){
    const q=$('#search-input').value.trim().toLocaleLowerCase('pl');const data=loginRows.filter(x=>(!selectedRoom||x.room===rooms.find(r=>r.id===selectedRoom)?.name)&&(!q||[x.user,x.account,x.computer,x.room,x.type].join(' ').toLocaleLowerCase('pl').includes(q)));
    rows=data.map(x=>`<tr><td class="last-seen">${dateFmt(x.when)}</td><td><div class="user-cell"><span class="tiny-avatar">${initials(x.account)}</span><div><strong class="computer-name">${x.user}</strong><div class="computer-meta">${x.account}</div></div></div></td><td class="computer-name">${x.computer}</td><td>${x.room}</td><td>${loginPill(x.type)}</td><td><button class="row-menu">···</button></td></tr>`);$('#result-count').textContent=`${data.length} przykładowych zdarzeń · zakres ostatnich 24 godzin`;
  } else if(activeView==='users'){
    const q=$('#search-input').value.trim().toLocaleLowerCase('pl');const data=people.filter(x=>(!q||[x.user,x.name,x.role,x.room].join(' ').toLocaleLowerCase('pl').includes(q))&&(!selectedRoom||x.room===rooms.find(r=>r.id===selectedRoom)?.name));
    rows=data.map(x=>`<tr><td><div class="user-cell"><span class="tiny-avatar">${initials(x.user)}</span><div><strong class="computer-name">${x.name}</strong><div class="computer-meta">${x.user}</div></div></div></td><td>${x.role}</td><td class="last-seen">${x.last}</td><td>${x.room}</td><td><span class="pill online">${x.state}</span></td><td><button class="row-menu">···</button></td></tr>`);$('#result-count').textContent=`${data.length} fikcyjnych kont`;
  } else {
    const q=$('#search-input').value.trim().toLocaleLowerCase('pl');const data=auditRows.filter(x=>!q||[x.actor,x.action,x.target,x.result].join(' ').toLocaleLowerCase('pl').includes(q));
    rows=data.map(x=>`<tr><td class="last-seen">${dateFmt(x.when)}</td><td><div class="user-cell"><span class="tiny-avatar">${initials(x.actor)}</span>${x.actor}</div></td><td>${x.action}</td><td class="computer-name">${x.target}</td><td>${x.result==='Symulacja'?'<span class="pill unknown">Symulacja</span>':'<span class="pill online">Sukces</span>'}</td><td><button class="row-menu">···</button></td></tr>`);$('#result-count').textContent=`${data.length} przykładowych wpisów audytu`;
  }
  body.innerHTML=rows.length?rows.join(''):`<tr><td colspan="7"><div class="empty-state">Brak wyników dla wybranych filtrów.</div></td></tr>`;
  body.querySelectorAll('[data-device]').forEach(row=>row.addEventListener('click',()=>showDevice(row.dataset.device)));
}
function statusPill(status){const cls=status==='Online'?'online':status==='Offline'?'offline':'unknown';return `<span class="pill ${cls}">${status}</span>`}
function loginPill(type){return type==='Logowanie'?'<span class="pill online">Logowanie</span>':type==='Wylogowanie'?'<span class="pill unknown">Wylogowanie</span>':'<span class="pill offline">Nieudana próba</span>'}
function initials(value){return String(value).split(/[.\s-]/).filter(Boolean).slice(0,2).map(s=>s[0].toUpperCase()).join('')}
function selectRoom(id){selectedRoom=selectedRoom===id?null:id;updateRoomFilter();setView(activeView==='overview'?'computers':activeView);renderTable();}
function updateRoomFilter(){const room=rooms.find(r=>r.id===selectedRoom);$('#room-filter-label').textContent=room?room.name:'Wszystkie pracownie';document.querySelectorAll('.room-link').forEach(b=>b.classList.toggle('selected',b.dataset.room===selectedRoom));}
function showDevice(name){
  const d=devices.find(x=>x.name===name);if(!d)return;
  $('#dialog-content').innerHTML=`<div class="eyebrow">SZCZEGÓŁY URZĄDZENIA</div><h2 class="detail-title">${d.name}</h2><p class="detail-sub">${d.room?rooms.find(r=>r.id===d.room).name:'Poza przypisaną pracownią'} · dane przykładowe</p><div class="detail-grid"><div class="detail-field"><span>Stan ostatniego pomiaru</span><strong>${statusPill(d.status)}</strong></div><div class="detail-field"><span>Adres IP</span><strong>${d.ip||'Brak danych'}</strong></div><div class="detail-field"><span>Ostatni użytkownik</span><strong>${d.user}</strong></div><div class="detail-field"><span>Ostatnia aktywność</span><strong>${d.lastSeen?dateFmt(d.lastSeen):'Nieznana'}</strong></div><div class="detail-field"><span>System operacyjny</span><strong>${d.os}</strong></div><div class="detail-field"><span>Źródło</span><strong>Inwentarz demonstracyjny</strong></div></div><div class="detail-actions"><button class="button button-secondary" data-action="lockComputer">Zablokuj (demo)</button><button class="button button-primary" data-action="restartComputer">Restart (demo)</button></div>`;
  $('#dialog-content').querySelectorAll('[data-action]').forEach(b=>b.addEventListener('click',()=>openAction(b.dataset.action,name)));
  $('#detail-dialog').showModal();
}
function openAction(action,target){$('#detail-dialog').close();const names={restartComputer:'Restart komputera',lockComputer:'Blokada komputera'};$('#action-title').textContent=names[action]||'Symulacja akcji';$('#action-description').textContent=`Przygotowano podgląd operacji „${names[action]}” dla urządzenia ${target}. W prawdziwej aplikacji akcja wymagałaby uprawnień i osobnego potwierdzenia.`;$('#action-dialog').showModal()}
function exportCsv(){
  if(activeView!=='overview'&&activeView!=='computers'){showToast('Eksport CSV w tej wersji demonstracyjnej jest dostępny dla listy komputerów.');return}
  const data=filteredDevices();const csv=[['Komputer','Pracownia','Stan','Adres IP','Ostatni użytkownik','System'],...data.map(d=>[d.name,d.room?rooms.find(r=>r.id===d.room).name:'Poza pracownią',d.status,d.ip||'',d.user,d.os])].map(row=>row.map(v=>`"${String(v).replaceAll('"','""')}"`).join(';')).join('\r\n');
  const a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['\ufeff'+csv],{type:'text/csv;charset=utf-8'}));a.download='zse-admin-demo-komputery.csv';a.click();URL.revokeObjectURL(a.href);showToast('Pobrano przykładowy plik CSV.');
}
function showDemoInfo(){showToast('To interaktywny podgląd beta. Dane są fikcyjne, a akcje jedynie symulowane — nic nie łączy się z Active Directory.');}
function showToast(message){const el=$('#toast');el.textContent=message;el.classList.add('show');clearTimeout(toastTimer);toastTimer=setTimeout(()=>el.classList.remove('show'),3500)}
init();
