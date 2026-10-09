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
- Databaza: zatial nie je implementovana; planovane EF Core a PostgreSQL alebo SQL Server.
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
- Docasny `InMemoryDeviceRepository` v `Infrastructure`.
- Registracia sluzieb cez `AddApplication()` a `AddInfrastructure()`, pouzita v `Server/Program.cs`.

## Aktualne funkcne casti systemu

- Solution sa zostavi na .NET 10.
- xUnit testovaci projekt je funkcny.
- WPF projekt sa zostavi ako `net10.0-windows`, spusti sa a zobrazi prazdne hlavne okno so stavovym riadkom.
- `DeviceService` vie zaregistrovat zariadenie a vratit jedno alebo vsetky zariadenia; data su len v pamati a po restarte sa stratia.
- Server sa spusti a ma zaregistrovane aplikacne a infrastrukturne sluzby, ale zatial ich nevystavuje cez ziadny endpoint.
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
- `DeviceService` je `Scoped`, `InMemoryDeviceRepository` je `Singleton`; pri prechode na EF Core bude repozitar `Scoped`.
- MVVM zaklad (`ViewModelBase`, `RelayCommand`) je vlastny, bez kniznice typu CommunityToolkit.Mvvm.
- `RelayCommand` nepouziva `CommandManager.RequerySuggested`; zmenu `CanExecute` oznamuje ViewModel cez `RaiseCanExecuteChanged`, aby boli prikazy testovatelne bez WPF runtime.
- `App.xaml` nema `StartupUri`; hlavne okno otvara `App.OnStartup` cez DI.

## Databaza a migracie

- Databazovy model nie je implementovany.
- EF Core nie je nakonfigurovany.
- Migracie neexistuju.

## Testy a zostavenie

- Posledny restore: `dotnet restore NetworkMonitoringSystem.slnx` uspesny po povoleni pristupu na NuGet.
- Posledny build: `dotnet build NetworkMonitoringSystem.slnx` uspesny, 0 warningov, 0 chyb.
- Posledne testy: `dotnet test NetworkMonitoringSystem.slnx --no-build` uspesne.
- Vysledok testov: 12 testov, 12 uspesnych, 0 zlyhanych.
- Datum overenia: 2026-10-09.

## Git stav

- Aktualna vetva: `main`.
- Vzdialeny repozitar: `origin` -> `https://github.com/ondrejkocun/network_monitoring_and_management_system.git`.
- Posledny relevantny commit: `670d7e9 Initialize solution structure and WPF MVVM skeleton`, pushnuty na `origin/main`.
- Aktualny stav: aplikacne rozhrania pre zariadenia este nie su commitnute.

## Zname problemy a technicky dlh

- Zatial neexistuje databazova vrstva; `InMemoryDeviceRepository` je docasna nahrada.
- Nie je osetrena duplicita zariadeni podla `HostName`.
- Zatial neexistuje klient-server komunikacia.
- WPF ma len MVVM kostru; `MainWindowViewModel` zatial nema ziadne prikazy ani realne data.
- `NetworkMonitoringSystem.Agent` pouziva balicek `Microsoft.Extensions.Hosting` vo verzii `8.0.1`; build na .NET 10 je funkcny, ale neskor moze byt vhodne zosuladit verziu balicka s .NET 10.
- xUnit balicky su zo sablony a build/test presli; neskor moze byt vhodne aktualizovat ich na aktualne verzie.

## Naposledy vykonana praca

Do `Application` sa doplnili rozhrania a sluzba pre zariadenia (`IDeviceRepository`, `IDeviceService`, `DeviceService`, `DeviceDto`), do `Infrastructure` docasny `InMemoryDeviceRepository`. Obe vrstvy maju vlastnu DI registraciu a server ich pouziva. Pridane boli testy pre sluzbu a repozitar. Etapa 1 je tym dokoncena.

## Odporucany dalsi krok

Rozhodnut medzi PostgreSQL a SQL Server a zacat Etapu 2: EF Core, `DbContext`, repozitar nad databazou a prva migracia. Pred dalsou etapou skontrolovat `PROJECT_STATUS.md`, `TODO.md`, skutocny Git stav a vysledok build/test.
