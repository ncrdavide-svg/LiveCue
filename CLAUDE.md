# LiveCue — note per Claude

App di show control per Windows in stile QLab (WPF, .NET 9, LibVLCSharp). L'utente parla **italiano**: rispondi in italiano.

**Stato del lavoro, cronologia e prossimi passi: leggi `docs/STATO-PROGETTO.md` prima di iniziare** e aggiornalo alla fine di ogni sessione di lavoro significativa.

## Comandi

```
dotnet build LiveCue.sln
dotnet test                                   # xUnit, tests/LiveCue.Core.Tests
dotnet run --project src/LiveCue.App
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0   # → artifacts\installer\LiveCue-Setup-<ver>.exe
python tools/make_icon.py                     # rigenera Assets/LiveCue.ico (serve Pillow)
```

Requisiti: .NET 9 SDK, Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

## Struttura

- `src/LiveCue.Core` — modelli e logica, **nessuna dipendenza da WPF/LibVLC** (tutto ciò che è testabile va qui):
  `CueEngine` (GO, pre/post-wait, Continua/Parti insieme), `DisplayManager` (monitor via Win32, `ResolveOutputBounds`), `ProjectFile` (`.livecue` JSON), `TimeInput` (parsing tempi), `SystemMonitor`.
- `src/LiveCue.App` — UI WPF, `MediaPlayerCuePlayer` (implementa `ICuePlayer` con LibVLC), una `VideoOutputWindow` per cue video, anteprima monitor via DWM.
- `tests/LiveCue.Core.Tests` — test di Core con `FakeCuePlayer`.
- `installer/` — script Inno Setup + script di build.

## Convenzioni

- Testi dell'interfaccia in **italiano**; codice, commenti e messaggi di commit in **inglese**.
- I commenti spiegano il *perché* (vincoli, bug evitati), come nel codice esistente.
- Ogni correzione di logica in Core arriva con un test.
- Commit su branch, mai direttamente su `master`.

## Insidie note (non reintrodurle)

- **Coordinate:** l'app è Per-Monitor-V2 DPI aware. `DisplayInfo`/`ScreenBounds` sono pixel reali; le finestre di uscita si posizionano con `SetWindowPos`, non con Left/Top WPF.
- **LibVLC:** gli eventi arrivano su thread di VLC. Non chiamare il player da lì; usare `TaskCompletionSource` con `RunContinuationsAsynchronously` e il Dispatcher per la UI. Stop/PANIC generano `Stopped` (non `EndReached`); file illeggibili generano `EncounteredError`.
- **`ICuePlayer.PlayAsync`** termina alla fine del *primo giro* (le cue in loop continuano dopo); su errore termina subito, così le catene "Continua" non si bloccano.
- **Monitor di uscita:** non usare mai il monitor principale (quello dell'operatore) come ripiego per una cue con schermo mancante.
- **`CueList`:** la regola "l'ultima cue non continua" NON va applicata sulle aggiunte, altrimenti il caricamento di un progetto azzera tutti i ContinueMode.
- **Tempi:** pre/post-wait si inseriscono in secondi tramite `TimeInput`/`SecondsConverter`; il binding diretto a `TimeSpan` legge "5" come 5 giorni.
- **Statistiche di sistema:** la lettura dei contatori GPU dura ~0,5 s → solo su thread in background.
- **Installer:** la publish va in una cartella temporanea corta: da percorsi lunghi i plugin VLC superano i 260 caratteri e Inno Setup fallisce.
- I file `.livecue` salvano i percorsi media **relativi** al progetto.
