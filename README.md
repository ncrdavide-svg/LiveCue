<p align="center"><img src="src/LiveCue.App/Assets/LiveCue.png" width="160" alt="LiveCue"></p>

<h1 align="center">LiveCue</h1>

<p align="center">App di show control per Windows in stile QLab: una lista di cue audio e video da lanciare con <b>GO</b>, con uscite video su più monitor (anche "monitor virtuali", cioè zone di uno schermo reale, trasparenti o no) e un pulsante <b>PANIC</b> (ESC) che ferma tutto.</p>

<p align="center">
  <a href="https://github.com/ncrdavide-svg/LiveCue/releases/latest/download/LiveCue-Setup.exe">
    <img src="docs/download-button.png" width="440" alt="⬇ Scarica LiveCue — installer per Windows 10/11, ultima versione">
  </a>
</p>

<p align="center">
  <img src="https://img.shields.io/github/v/release/ncrdavide-svg/LiveCue?label=versione&color=2ea44f" alt="versione"><br>
  Il pulsante scarica direttamente <code>LiveCue-Setup.exe</code> · <a href="https://github.com/ncrdavide-svg/LiveCue/releases">tutte le versioni e le novità</a>
</p>

---

## Come si installa

1. **Clicca il pulsante verde qui sopra**: parte subito il download di `LiveCue-Setup.exe`, sempre l'ultima versione.
2. **Apri il file scaricato** (di solito è nella cartella *Download*).
3. Se compare la finestra blu **«Windows ha protetto il PC»**, clicca **Ulteriori informazioni** e poi **Esegui comunque**: succede perché l'installer non ha una firma digitale a pagamento, non perché sia pericoloso.
4. Segui l'installazione: puoi installare **per tutti gli utenti** (servono i permessi di amministratore) o **solo per te**.

Il setup contiene già .NET e VLC: sul PC non serve installare altro. Per aggiornare basta installare la versione nuova sopra quella vecchia.

## Requisiti

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

LibVLC è incluso tramite NuGet: non serve installare VLC.

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

Crea in `artifacts\installer\` sia `LiveCue-Setup-1.0.0.exe` sia `LiveCue-Setup.exe` (stesso file, nome fisso: **vanno caricati tutti e due nella Release**, il pulsante di download punta a `releases/latest/download/LiveCue-Setup.exe`) (serve [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`). Il setup contiene già .NET e VLC, quindi sui PC di destinazione non serve installare niente: funziona su Windows 10/11 a 64 bit (anche ARM64, in emulazione). Durante l'installazione si può scegliere "per tutti gli utenti" (richiede admin) o "solo per me".

## Struttura

| Progetto | Contenuto |
|---|---|
| `src/LiveCue.Core` | Modelli (cue, lista, monitor virtuali) e servizi: `CueEngine` (GO, pre/post-wait, Continua / Parti insieme), `DisplayManager` (monitor e risoluzioni via Win32), `ProjectFile` (salvataggio `.livecue`), `SystemMonitor` (CPU/RAM/GPU). Nessuna dipendenza da WPF o LibVLC. |
| `src/LiveCue.App` | Interfaccia WPF e riproduzione con LibVLC (`MediaPlayerCuePlayer`), una finestra di uscita per ogni cue video, anteprima live dei monitor via DWM. |
| `tests/LiveCue.Core.Tests` | Test xUnit della logica di `LiveCue.Core`. |

## Note d'uso

- **Continua**: finita la cue, parte la successiva. Se la cue è in **Loop**, la successiva parte alla fine del primo giro e la cue continua a girare finché non la fermi.
- **Parti insieme**: la cue successiva parte subito, insieme a questa.
- I file `.livecue` salvano anche i monitor virtuali, e i percorsi dei media relativi al file di progetto: tieni progetto e media nella stessa cartella e potrai spostarla o copiarla su un altro PC.

## Stato del progetto

Cronologia del lavoro, cosa è stato corretto e prossimi passi: [docs/STATO-PROGETTO.md](docs/STATO-PROGETTO.md).

## Supporta il progetto

Se LiveCue ti è utile, offrimi un caffè: [buymeacoffee.com/cin8](https://buymeacoffee.com/cin8)

Altre app e contatti: [GitHub](https://github.com/ncrdavide-svg)
