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

## Nasledujuce ulohy

- [ ] Navrhnut a zaviest zakladne aplikacne rozhrania.
  - Ciel: pripravit hranice medzi `Application`, `Infrastructure`, `Server`, `Agent` a `Desktop`.
  - Subory/projekty: `NetworkMonitoringSystem.Application`, pripadne testy.
  - Zavislosti: dokoncena zakladna struktura.
  - Overenie: build a jednotkove testy.

- [ ] Pripravit Etapu 2 - databazovy zaklad.
  - Ciel: nakonfigurovat EF Core a prvy model zariadenia pre perzistenciu.
  - Subory/projekty: `NetworkMonitoringSystem.Infrastructure`, `NetworkMonitoringSystem.Domain`, testy.
  - Zavislosti: rozhodnutie medzi PostgreSQL a SQL Server.
  - Overenie: izolovane databazove testy, ziadna zavislost od produkcnej databazy.

## Buduce rozsirenia

- [ ] Klient-server komunikacia.
  - Ciel: agent sa bezpecne identifikuje a posiela stav serveru.
  - Predbezna technologia: gRPC.
  - Overenie: test odpojenia a opatovneho pripojenia.

- [ ] Monitoring CPU, RAM, diskov a sietovych rozhrani.
  - Ciel: agent ziska zakladne systemove metriky.
  - Overenie: jednotkove testy mapperov a integračné testy podla moznosti.

- [ ] Historia stavov a vypadkov.
  - Ciel: ukladat historicke stavy, detegovat offline/online prechody.
  - Overenie: testy casovych hranic a vypoctu dostupnosti.

- [ ] WPF dashboard.
  - Ciel: zobrazit zoznam zariadeni, detail a stavy online/offline.
  - Overenie: build, ViewModel testy a manualna kontrola UI.

- [ ] Procesy, porty a aktivne spojenia.
  - Ciel: ziskat udaje z monitorovaneho zariadenia a osetrit chybajuce opravnenia.
  - Overenie: testy uspesnych aj chybovych scenarov.

- [ ] Analyza sietovej komunikacie cez TShark.
  - Ciel: overit technicku uskutocnitelnost a bezpecne filtrovanie vystupu.
  - Zavislosti: dostupnost TShark, opravnenia, vykon.
  - Overenie: prototyp a zdokumentovane limity.

- [ ] Pouzivatelia, autentifikacia a ACL.
  - Ciel: role, opravnenia a autorizacia chranenych operacii.
  - Overenie: testy pristupovych prav.

- [ ] Vzdialena sprava.
  - Ciel: povolene akcie, audit, zakaz lubovolnych shell prikazov.
  - Overenie: test neopravnenych poziadaviek a zlyhania komunikacie.

## Chyby a blokovane body

- [!] Vyber databazy este nie je rozhodnuty.
  - Moznosti: PostgreSQL alebo SQL Server.
  - Dopad: ovplyvni EF Core provider, migracie a testovaciu databazu.
  - Potrebne rozhodnutie: vybrat databazu pred Etapou 2.

- [!] Balicky pre agent a testy maju verzie zo sablon.
  - Stav: build a testy presli na .NET 10.
  - Riziko: neskor moze byt vhodne zosuladit verzie balickov s .NET 10.
  - Overenie: pri dalsich zmenach opakovat restore/build/test.
