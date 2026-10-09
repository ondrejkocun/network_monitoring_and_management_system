# Network Monitoring & Management System - stav projektu

## Projekt

Network Monitoring & Management System je bakalarsky projekt v C#/.NET zamerany na monitorovanie sietovej infrastruktury a vzdialenu spravu zariadeni v architekture klient-server.

Zavazny rozsah urcuje oficialne zadanie v `docs/zadanie.md`. Pokrytie zadania ulohami je v `TODO.md` v casti "Povinne casti podla zadania".

## Architektura a technologie

- Jazyk a platforma: C# / .NET 10.
- Desktop UI: WPF, planovane MVVM.
- Server: ASP.NET Core prazdny serverovy projekt, pripraveny pre buduce API alebo gRPC endpointy.
- Agent: .NET Worker Service pre buduce monitorovanie zariadenia a komunikaciu so serverom.
- Testy: xUnit.
- Databaza: PostgreSQL 17 cez EF Core 10 (Npgsql). Schvaleny datovy model je v `docs/datovy-model.md`; implementovana je jeho prva cast.
- Komunikacia: zatial nie je implementovana; predbezne odporucanie je gRPC pre agent-server komunikaciu.

## Projekty v solution

- `NetworkMonitoringSystem.Domain` - domenove modely a pravidla bez zavislosti od UI, databazy alebo siete.
- `NetworkMonitoringSystem.Application` - miesto pre aplikacne sluzby, use-cases, rozhrania a DTO.
- `NetworkMonitoringSystem.Infrastructure` - miesto pre databazu, repozitare a integracie.
- `NetworkMonitoringSystem.Server` - serverova cast systemu.
- `NetworkMonitoringSystem.Agent` - klientsky agent pre monitorovane zariadenia.
- `NetworkMonitoringSystem.Desktop` - WPF desktopova aplikacia.
- `NetworkMonitoringSystem.Tests` - automatizovane xUnit testy.

## Implementovane funkcionality

- Inicializovana solution struktura.
- Inicializovany Git repozitar.
- Pridany `.gitignore` pre .NET/WPF projekt.
- Vytvoreny prvy domenovy model `Device`.
- Vytvoreny prvy xUnit test pre zakladne pravidla modelu `Device`.
- Zakladna MVVM struktura vo WPF projekte: `Views/MainWindow`, `ViewModels/ViewModelBase`, `ViewModels/MainWindowViewModel`, `Commands/RelayCommand`.
- DI kontajner (`Microsoft.Extensions.DependencyInjection`) v `App.xaml.cs`; hlavne okno a jeho ViewModel sa vytvaraju cez DI.
- Aplikacna vrstva pre zariadenia: `IDeviceRepository`, `IDeviceService`, `DeviceService`, `DeviceDto`.
- `InMemoryDeviceRepository` v `Infrastructure`; pouzivaju ho uz len jednotkove testy.
- `Device` rozsireny o rezim sledovania (s agentom / bez agenta), IP adresu, stav, cas posledneho kontaktu, priznak povolenia a cas vytvorenia.
- `MonitoringSettings` s globalnym intervalom synchronizacie, prahom nedostupnosti, dobou uchovavania a poctom sledovanych procesov.
- EF Core `MonitoringDbContext`, `EfDeviceRepository` a prva migracia `InitialCreate`.
- `docker-compose.yml` s lokalnou vyvojovou databazou.
- Registracia sluzieb cez `AddApplication()` a `AddInfrastructure()`, pouzita v `Server/Program.cs`.

## Aktualne funkcne casti systemu

- Solution sa zostavi na .NET 10.
- xUnit testovaci projekt je funkcny.
- WPF projekt sa zostavi ako `net10.0-windows`, spusti sa a zobrazi prazdne hlavne okno so stavovym riadkom.
- `DeviceService` vie zaregistrovat zariadenie s agentom aj bez agenta a vratit jedno alebo vsetky zariadenia; na serveri sa ukladaju do PostgreSQL.
- Server sa spusti, vo vyvojovom prostredi pri starte aplikuje migracie a ma zaregistrovane aplikacne a infrastrukturne sluzby, ale zatial ich nevystavuje cez ziadny endpoint.
- Agent je zatial prazdny zaklad bez realnej funkcionality.

## Technicke rozhodnutia

- Projekt cieli na .NET 10, podla aktualnej poziadavky pouzivatela.
- Agent zatial podporuje len Windows; zber metrik, portov a procesov moze pouzivat Windows API.
- System sleduje aj zariadenia bez agenta; ich dostupnost kontroluje server.
- Interval synchronizacie stavov je globalny a spravuje ho server.
- Zvolena je vrstvena struktura `Domain`, `Application`, `Infrastructure`, `Server`, `Agent`, `Desktop`, aby boli oddelene domenove pravidla, aplikacna logika, infrastruktura a UI.
- `Domain` nema zavislosti na ostatne projekty.
- `Application` zavisi na `Domain`.
- `Infrastructure` zavisi na `Application` a `Domain`.
- `Server` zavisi na `Application` a `Infrastructure`.
- `Agent` a `Desktop` zatial zavisia na `Application`.
- Testy referencuju `Domain`, `Application`, `Infrastructure` a `Desktop`; testovaci projekt preto cieli na `net10.0-windows`.
- Rozhranie repozitara (`IDeviceRepository`) je v `Application`, implementacia v `Infrastructure`; aplikacna vrstva tak nezavisi od sposobu ukladania.
- Aplikacne sluzby vracaju DTO (`DeviceDto`), nie domenove objekty.
- Kazda vrstva registruje svoje sluzby vlastnou extension metodou (`AddApplication`, `AddInfrastructure`).
- `DeviceService`, `EfDeviceRepository` aj `MonitoringDbContext` su `Scoped`.
- Zariadenie s agentom musi mat hostname, zariadenie bez agenta musi mat IP adresu; druhy udaj je nepovinny.
- Stav zariadenia a rezim sledovania sa v databaze ukladaju ako text, nie cislo, aby boli citatelne priamo v SQL.
- IP adresa sa uklada v PostgreSQL type `inet`.
- Cas sa berie z `TimeProvider`, aby sa dal v testoch nahradit.
- Tabulky a stlpce maju predvolene nazvy EF Core (PascalCase), v SQL ich preto treba pisat v uvodzovkach.
- Server aplikuje migracie automaticky len v prostredi `Development`.
- Databazove testy bezia proti skutocnemu PostgreSQL v docasnom kontajneri (Testcontainers), nie proti nahrade typu SQLite.
- `dotnet-ef` je lokalny nastroj zapisany v `dotnet-tools.json`.
- MVVM zaklad (`ViewModelBase`, `RelayCommand`) je vlastny, bez kniznice typu CommunityToolkit.Mvvm.
- `RelayCommand` nepouziva `CommandManager.RequerySuggested`; zmenu `CanExecute` oznamuje ViewModel cez `RaiseCanExecuteChanged`, aby boli prikazy testovatelne bez WPF runtime.
- `App.xaml` nema `StartupUri`; hlavne okno otvara `App.OnStartup` cez DI.

## Databaza a migracie

- Tabulky: `Devices`, `MonitoringSettings` (vzdy jeden riadok, vklada ho migracia).
- Migracie: `InitialCreate` v `src/NetworkMonitoringSystem.Infrastructure/Persistence/Migrations`.
- Lokalna databaza: `docker compose up -d` spusti PostgreSQL na `localhost:5432`; pripojovaci retazec bez hesla je v `appsettings.Development.json`.
- Heslo k databaze nie je v repozitari. Kontajner ho cita zo suboru `.env` (`NMS_DB_PASSWORD`, vzor v `.env.example`), server z kluca `Database:Password` v user-secrets alebo z premennej prostredia `Database__Password`. Na novom pocitaci treba oboje nastavit na rovnaku hodnotu.
- Nova migracia: `dotnet tool restore`, potom `dotnet ef migrations add <Nazov> --project src/NetworkMonitoringSystem.Infrastructure --startup-project src/NetworkMonitoringSystem.Server --output-dir Persistence/Migrations`.
- Dalsie tabulky pribudnu podla poradia v `docs/datovy-model.md`.

## Testy a zostavenie

- Posledny restore: `dotnet restore NetworkMonitoringSystem.slnx` uspesny po povoleni pristupu na NuGet.
- Posledny build: `dotnet build NetworkMonitoringSystem.slnx` uspesny, 0 warningov, 0 chyb.
- Posledne testy: `dotnet test NetworkMonitoringSystem.slnx` uspesne. Styri databazove testy (`Category=Integration`) potrebuju beziaci Docker; bez neho sa daju vynechat cez `--filter "Category!=Integration"`.
- Vysledok testov: 34 testov, 34 uspesnych, 0 zlyhanych.
- Datum overenia: 2026-10-09.

## Git stav

- Aktualna vetva: `main`.
- Vzdialeny repozitar: `origin` -> `https://github.com/ondrejkocun/network_monitoring_and_management_system.git`.
- Posledny relevantny commit: `42102d1`, pushnuty na `origin/main`.
- Aktualny stav: navrh datoveho modelu a databazovy zaklad este nie su commitnute.

## Zname problemy a technicky dlh

- Stav zariadenia (`Status`, `LastSeenAt`, `IsEnabled`) sa zatial neda menit; pribudne so sledovanim dostupnosti.
- `MonitoringSettings` ma tabulku, ale zatial ziadnu sluzbu na citanie a zmenu.
- Nie je osetrena duplicita zariadeni podla `HostName` ani IP adresy.
- Zatial neexistuje klient-server komunikacia.
- WPF ma len MVVM kostru; `MainWindowViewModel` zatial nema ziadne prikazy ani realne data.
- `NetworkMonitoringSystem.Agent` pouziva balicek `Microsoft.Extensions.Hosting` vo verzii `8.0.1`; build na .NET 10 je funkcny, ale neskor moze byt vhodne zosuladit verziu balicka s .NET 10.
- xUnit balicky su zo sablony a build/test presli; neskor moze byt vhodne aktualizovat ich na aktualne verzie.

## Naposledy vykonana praca

Schvalil sa datovy model (`docs/datovy-model.md`) a zaviedla sa jeho prva cast: rozsireny `Device`, `MonitoringSettings`, EF Core s PostgreSQL, `EfDeviceRepository`, migracia `InitialCreate` a lokalna databaza cez Docker. Server uz uklada zariadenia do databazy. Pridane boli jednotkove testy a databazove testy proti skutocnemu PostgreSQL.

## Odporucany dalsi krok

Zacat klient-server komunikaciu: rozhodnut technologiu (predbezne gRPC), registraciu a overenie agenta a prve pravidelne hlasenie stavu serveru. Pred dalsou etapou skontrolovat `PROJECT_STATUS.md`, `TODO.md`, skutocny Git stav a vysledok build/test.
