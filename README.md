# LiveCue

App di show control per Windows in stile QLab: una lista di cue audio e video da lanciare con **GO**, con uscite video su piÃ¹ monitor (anche "monitor virtuali", cioÃ¨ zone di uno schermo reale) e un pulsante **PANIC** (ESC) che ferma tutto.

## Requisiti

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

LibVLC Ã¨ incluso tramite NuGet: non serve installare VLC.

## Avvio

```
dotnet run --project src/LiveCue.App
```

## Test

```
dotnet test
```

## Installer per altri PC

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0
```

Crea `artifacts\installer\LiveCue-Setup-1.0.0.exe` (serve [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`). Il setup contiene giÃ  .NET e VLC, quindi sui PC di destinazione non serve installare niente: funziona su Windows 10/11 a 64 bit (anche ARM64, in emulazione). Durante l'installazione si puÃ² scegliere "per tutti gli utenti" (richiede admin) o "solo per me".

## Struttura

| Progetto | Contenuto |
|---|---|
| `src/LiveCue.Core` | Modelli (cue, lista, monitor virtuali) e servizi: `CueEngine` (GO, pre/post-wait, Continua / Parti insieme), `DisplayManager` (monitor e risoluzioni via Win32), `ProjectFile` (salvataggio `.livecue`), `SystemMonitor` (CPU/RAM/GPU). Nessuna dipendenza da WPF o LibVLC. |
| `src/LiveCue.App` | Interfaccia WPF e riproduzione con LibVLC (`MediaPlayerCuePlayer`), una finestra di uscita per ogni cue video, anteprima live dei monitor via DWM. |
| `tests/LiveCue.Core.Tests` | Test xUnit della logica di `LiveCue.Core`. |

## Note d'uso

- **Continua**: finita la cue, parte la successiva. Se la cue Ã¨ in **Loop**, la successiva parte alla fine del primo giro e la cue continua a girare finchÃ© non la fermi.
- **Parti insieme**: la cue successiva parte subito, insieme a questa.
- I file `.livecue` salvano anche i monitor virtuali, e i percorsi dei media relativi al file di progetto: tieni progetto e media nella stessa cartella e potrai spostarla o copiarla su un altro PC.

## Stato del progetto

Cronologia del lavoro, cosa è stato corretto e prossimi passi: [docs/STATO-PROGETTO.md](docs/STATO-PROGETTO.md).
