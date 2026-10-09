# Network Monitoring & Management System - stav projektu

## Projekt

Network Monitoring & Management System je bakalarsky projekt v C#/.NET zamerany na monitorovanie sietovej infrastruktury a vzdialenu spravu zariadeni v architekture klient-server.

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

## Aktualne funkcne casti systemu

- Solution sa zostavi na .NET 10.
- xUnit testovaci projekt je funkcny.
- WPF projekt sa zostavi ako `net10.0-windows`, spusti sa a zobrazi prazdne hlavne okno so stavovym riadkom.
- Server, agent, application a infrastructure projekty su zatial prazdne zaklady bez realnej funkcionality.

## Technicke rozhodnutia

- Projekt cieli na .NET 10, podla aktualnej poziadavky pouzivatela.
- Zvolena je vrstvena struktura `Domain`, `Application`, `Infrastructure`, `Server`, `Agent`, `Desktop`, aby boli oddelene domenove pravidla, aplikacna logika, infrastruktura a UI.
- `Domain` nema zavislosti na ostatne projekty.
- `Application` zavisi na `Domain`.
- `Infrastructure` zavisi na `Application` a `Domain`.
- `Server` zavisi na `Application` a `Infrastructure`.
- `Agent` a `Desktop` zatial zavisia na `Application`.
- Testy referencuju `Domain` a `Desktop`; testovaci projekt preto cieli na `net10.0-windows`.
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
- Vysledok testov: 7 testov, 7 uspesnych, 0 zlyhanych.
- Datum overenia: 2026-10-09.

## Git stav

- Aktualna vetva: `main`.
- Vzdialeny repozitar: `origin` -> `https://github.com/ondrejkocun/network_monitoring_and_management_system.git`.
- Posledny relevantny commit: ziadny, repozitar zatial nema commit.
- Aktualny stav: vytvorene subory su untracked.

## Zname problemy a technicky dlh

- Zatial neexistuje databazova vrstva.
- Zatial neexistuje klient-server komunikacia.
- WPF ma len MVVM kostru; `MainWindowViewModel` zatial nema ziadne prikazy ani realne data.
- `NetworkMonitoringSystem.Agent` pouziva balicek `Microsoft.Extensions.Hosting` vo verzii `8.0.1`; build na .NET 10 je funkcny, ale neskor moze byt vhodne zosuladit verziu balicka s .NET 10.
- xUnit balicky su zo sablony a build/test presli; neskor moze byt vhodne aktualizovat ich na aktualne verzie.

## Naposledy vykonana praca

Do WPF projektu sa doplnila zakladna MVVM struktura: `MainWindow` sa presunul do `Views`, pridali sa `ViewModelBase`, `MainWindowViewModel` a `RelayCommand`, a `App.xaml.cs` sklada aplikaciu cez DI kontajner. Pridane boli testy pre ViewModel a prikaz.

## Odporucany dalsi krok

Dokoncit Etapu 1 navrhom zakladnych aplikacnych rozhrani v `Application` bez databazy. Pred dalsou etapou skontrolovat `PROJECT_STATUS.md`, `TODO.md`, skutocny Git stav a vysledok build/test.
