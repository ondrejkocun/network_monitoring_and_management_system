# TODO - Network Monitoring & Management System

## Aktualne ulohy

- [x] Inicializovat Git repozitar.
  - Subory/projekty: `.git/`.
  - Overenie: `git status --short --branch` ukazuje vetvu `main` bez commitov.

- [x] Vytvorit zakladnu .NET 10 solution strukturu.
  - Subory/projekty: `NetworkMonitoringSystem.slnx`, `src/*`, `tests/*`.
  - Overenie: `dotnet build NetworkMonitoringSystem.slnx --no-restore` presiel bez chyb.

- [x] Vytvorit `.gitignore`.
  - Subory/projekty: `.gitignore`.
  - Overenie: `bin/` a `obj/` vystupy sa nezobrazuju ako zmeny v Git stave.

- [x] Pridat prvy domenovy model a test.
  - Subory/projekty: `src/NetworkMonitoringSystem.Domain/Devices/Device.cs`, `tests/NetworkMonitoringSystem.Tests/UnitTest1.cs`.
  - Overenie: `dotnet test NetworkMonitoringSystem.slnx --no-build --verbosity normal` presiel, 1 test uspesny.

- [x] Vytvorit dokumentaciu stavu projektu.
  - Subory/projekty: `PROJECT_STATUS.md`, `TODO.md`.
  - Overenie: subory obsahuju aktualny stav, testy, Git stav a dalsi krok.

- [x] Doplnenie zakladnej MVVM struktury pre WPF.
  - Subory/projekty: `src/NetworkMonitoringSystem.Desktop/Views`, `ViewModels`, `Commands`, `App.xaml.cs`, `tests/NetworkMonitoringSystem.Tests/Desktop`.
  - Overenie: build celej solution bez warningov, 7 testov uspesnych, aplikacia sa spusti a zobrazi hlavne okno.

- [x] Navrhnut a zaviest zakladne aplikacne rozhrania.
  - Subory/projekty: `src/NetworkMonitoringSystem.Application/Devices`, `src/NetworkMonitoringSystem.Infrastructure/Devices`, `DependencyInjection.cs` v oboch projektoch, `src/NetworkMonitoringSystem.Server/Program.cs`, testy v `tests/NetworkMonitoringSystem.Tests/Application` a `Infrastructure`.
  - Overenie: build bez warningov, 12 testov uspesnych, server sa spusti s novymi DI registraciami.
  - Poznamka: rozhrania pre `Agent` a `Desktop` sa doplnia az s klient-server komunikaciou.

## Nasledujuce ulohy

- [x] Pripravit Etapu 2 - databazovy zaklad.
  - Subory/projekty: `src/NetworkMonitoringSystem.Infrastructure/Persistence`, `EfDeviceRepository`, `src/NetworkMonitoringSystem.Domain/Devices`, `Domain/Monitoring`, `docker-compose.yml`, `dotnet-tools.json`.
  - Overenie: 34 testov uspesnych vratane 4 databazovych proti PostgreSQL v kontajneri; server po starte vytvoril tabulky `Devices` a `MonitoringSettings`.

## Povinne casti podla zadania

Vsetky ulohy v tejto casti vyplyvaju z `docs/zadanie.md` a su povinne, nie volitelne rozsirenia. Cislo v zatvorke odkazuje na bod obsahu zadania.

- [x] Navrh datoveho modelu (bod 2).
  - Stav: schvaleny 2026-10-09, je v `docs/datovy-model.md`. Tabulky sa zavadzaju postupne podla poradia v tom dokumente.
  - Ciel: entity pre zariadenia, ich stavy, systemove prostriedky, porty, procesy, vypadky a udalosti; vztahy medzi nimi.
  - Vystup: diagram datoveho modelu pouzitelny aj v pisomnej casti prace.
  - Overenie: model pokryva vsetky udaje vymenovane v zadani.

- [ ] Klient-server komunikacia (bod 2, 3).
  - Ciel: agent sa bezpecne identifikuje a posiela stav serveru.
  - Predbezna technologia: gRPC.
  - Overenie: test odpojenia a opatovneho pripojenia.

- [ ] Sledovanie dostupnosti zariadeni (bod 3).
  - Ciel: server vie, ci je zariadenie online alebo offline, podla pravidelnych hlaseni agenta.
  - Zariadenia bez agenta: server ich kontroluje sam cez ping (ICMP) a pripadne test TCP portu; zariadenie preto potrebuje priznak sposobu sledovania (s agentom / bez agenta) a IP adresu.
  - Overenie: test prechodu online/offline pri vypadku hlaseni aj pri neodpovedajucom pingu.

- [ ] Monitoring CPU, RAM, diskov a sietovych rozhrani (bod 3).
  - Ciel: agent ziska zakladne systemove metriky.
  - Overenie: jednotkove testy mapperov a integracne testy podla moznosti.

- [ ] Porty, procesy a aktivne spojenia (bod 3).
  - Ciel: ziskat otvorene porty, beziace procesy a aktivne spojenia; priradit port a spojenie k procesu, ktory ho pouziva ("suvisiace procesy").
  - Overenie: testy uspesnych aj chybovych scenarov vratane chybajucich opravneni.

- [ ] Sledovanie sietovej komunikacie (bod 3).
  - Ciel: zaznamenat sietovu komunikaciu zariadenia; je to povinna cast, nie len overenie uskutocnitelnosti.
  - Predbezna technologia: TShark; ak sa ukaze ako nevhodny, treba zvolit nahradu a zdovodnit ju.
  - Zavislosti: dostupnost TShark, opravnenia, vykon.
  - Overenie: funkcny zber a zdokumentovane limity.

- [ ] Zaznamenavanie stavov v nastavitelnych intervaloch (bod 4).
  - Ciel: interval synchronizacie stavov je konfigurovatelny, nie napevno v kode; stavy sa ukladaju do databazy ako historia.
  - Interval je globalny, ulozeny na serveri; agenti ho dostavaju od servera a pouziva ho aj kontrola zariadeni bez agenta.
  - Overenie: test, ze zmena intervalu zmeni frekvenciu zapisov.

- [ ] Evidencia vypadkov a dalsich udalosti (bod 4).
  - Ciel: detegovat offline/online prechody, ukladat vypadky so zaciatkom a koncom, viest vseobecny zaznam udalosti (nielen vypadkov).
  - Overenie: testy casovych hranic a vypoctu dostupnosti.

- [ ] Pouzivatelia, autentifikacia a ACL (bod 5).
  - Ciel: role, opravnenia a autorizacia chranenych operacii; sprava opravneni je sucastou systemu, nie len pevna konfiguracia.
  - Overenie: testy pristupovych prav.

- [ ] Vzdialena sprava (bod 5).
  - Ciel: vykonavanie vybranych prikazov podla opravneni, audit, zakaz lubovolnych shell prikazov.
  - Zoznam prikazov: pozri cast "Schvalene navrhy".
  - Overenie: test neopravnenych poziadaviek a zlyhania komunikacie.

- [ ] WPF dashboard.
  - Ciel: zobrazit zoznam zariadeni, detail, stavy online/offline, historiu a udalosti; rozhranie pre vzdialenu spravu a ACL.
  - Overenie: build, ViewModel testy a manualna kontrola UI.

- [ ] Modelova sietova infrastruktura a testovanie (bod 6).
  - Ciel: pripravit testovacie prostredie s viacerymi zariadeniami (napr. virtualne stroje) a otestovat v nom cely system.
  - Vystup: popis topologie, testovacie scenare a ich vysledky pre pisomnu cast prace.
  - Overenie: zdokumentovane scenare vratane vypadku zariadenia a neopravneneho pristupu.

- [ ] Zhodnotenie bezpecnosti (bod 6).
  - Ciel: sifrovana komunikacia agent-server, overenie identity agenta, ochrana ulozenych prihlasovacich udajov, audit vzdialenych prikazov.
  - Overenie: zoznam hrozieb a opatreni, ktory sa da pouzit v pisomnej casti.

## Pisomna cast prace

Nie su to programatorske ulohy, ale zadanie ich vyzaduje a kod ich ma podporit.

- [ ] Prieskum existujucich nastrojov na monitorovanie siete a vzdialenu spravu (bod 1).
- [ ] Definovanie zakladnych poziadaviek systemu (bod 1).
- [ ] Popis architektury klient-server a datoveho modelu (bod 2).
- [ ] Zhodnotenie funkcnosti, bezpecnosti a moznosti dalsieho rozsirenia (bod 6).

## Rozhodnutia k zadaniu

Rozhodnute pouzivatelom 2026-10-09:

- Agent zatial bezi len na Windows.
- Sleduju sa aj zariadenia bez agenta (napr. router, switch); kontroluje ich server.
- Interval synchronizacie stavov sa nastavuje globalne pre cely system.

- Databaza je PostgreSQL.

## Schvalene navrhy

Schvalene pouzivatelom 2026-10-09.

- Zoznam prikazov vzdialenej spravy.
  - Restart zariadenia.
  - Vypnutie zariadenia.
  - Ukoncenie procesu podla PID.
  - Spustenie, zastavenie a restart Windows sluzby.
  - Okamzita synchronizacia stavu mimo intervalu.
  - Diagnostika zo zariadenia: ping a traceroute na zadanu adresu.
  - Vyprazdnenie DNS cache.
  - Pravidla: pevny zoznam prikazov s overenymi parametrami, ziadny volny shell; kazdy prikaz ma vlastne opravnenie v ACL a zapis v audite.
  - Prikazy sa tykaju len zariadeni s agentom.

- Modelova sietova infrastruktura.
  - Virtualne stroje v Hyper-V na jednom pocitaci, interny virtualny prepinac, jedna podsiet.
  - Stroj 1: server, databaza a desktopova aplikacia.
  - Stroj 2: Windows 11 s agentom (bezna pracovna stanica).
  - Stroj 3: Windows Server s agentom (zariadenie s viacerymi sluzbami a otvorenymi portami).
  - Stroj 4: zariadenie bez agenta (maly Linux alebo virtualny router), sledovane len zo servera.
  - Scenare: vypnutie zariadenia, odpojenie od siete, zastavenie sluzby, zataz CPU, otvorenie noveho portu, zmena intervalu, neopravneny prikaz, vypadok servera a opatovne pripojenie agenta.

## Chyby a blokovane body

- [!] Balicky pre agent a testy maju verzie zo sablon.
  - Stav: build a testy presli na .NET 10.
  - Riziko: neskor moze byt vhodne zosuladit verzie balickov s .NET 10.
  - Overenie: pri dalsich zmenach opakovat restore/build/test.
