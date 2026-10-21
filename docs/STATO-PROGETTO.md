# LiveCue — Report del lavoro e stato del progetto

*Ultimo aggiornamento: 21 ottobre 2026*

Questo documento riassume tutto il lavoro fatto su LiveCue con Claude Code, il punto in cui siamo arrivati e come riprendere da un altro PC. **Va aggiornato alla fine di ogni sessione di lavoro**, da qualsiasi PC: è l'unico "ponte" tra le sessioni (le conversazioni con Claude non si vedono da un PC all'altro).

---

## 1. Dove siamo (in breve)

- **Tutto è in `master`** (21/10): tutto il lavoro fino a PR #18 compresa, più i quattro branch del 20/10 uniti il 21/10 (§5e). Build pulita, **81 test su 81** superati.
- **Ultima versione pubblicata: [Release v1.1.0](https://github.com/ncrdavide-svg/LiveCue/releases/tag/v1.1.0)** (21/10), con `LiveCue-Setup-1.1.0.exe` allegato (self-contained win-x64, non serve installare nient'altro). Contiene tutto fino al §5e. Si installa sopra la 1.0.0 come aggiornamento.
  - Link da distribuire, sempre all'ultima versione: https://github.com/ncrdavide-svg/LiveCue/releases/latest
  - Il tag `v1.1.0` punta a `05be40d`, il commit da cui è stato creato il setup. SHA256 del setup: `4815A2E36B8F660183E0FE99D3748E91F64C8619A007FDF55B324575498DE4A1`.
  - La versione nel codice (`LiveCue.App.csproj`) è ora 1.1.0. Per la prossima release: aumentarla, rigenerare il setup con `-Version` uguale, creare il tag e la release (`gh release create`).
  - Release precedente: v1.0.0 (19/10).
- Branch remoti già uniti ma non ancora cancellati su GitHub (si possono eliminare quando capita): `feature/fade-cue`, `feature/installer`, `feature/monitor-identify-rename-screentest`, `feature/monitor-toggle-and-image-cue-v2`, `feature/stop-cue-and-monitor-blackout`, `feature/undo-redo`, `feature/volume-slider-and-maximized-startup`, `fix/panic-deadlock`, `fix/review-issues`, `fix/video-window-focus-steal`, `docs/update-status`, `docs/sync-status-20-10`, `feature/transparent-overlay-virtual-screens`, `fix/schermo-reset-on-add-cue`, `feature/space-go-fix-and-onair-highlight`, `feature/cue-type-icon-in-tipo-column`, `feature/buymeacoffee-support-dialog`.
- `feature/monitor-toggle-and-image-cue` è un vecchio **WIP superato** dalla v2 (PR #9) e **non è stato unito**: si può cancellare.
- **Setup:** si genera con lo script dell'installer (vedi §8); non è nel repository (`artifacts/` è escluso da Git), va rigenerato su ogni PC — oppure si scarica l'ultima release già pronta da GitHub.

### Storia (in ordine)

| Quando | Commit / PR | Contenuto |
|---|---|---|
| — | `9772f6d` | Commit iniziale (codice originale) |
| 15/10 ufficio | `ea640e8` | Primo giro di correzioni + test + README (§2) |
| 15/10 ufficio | `2f355dd`, `ab00643` | Installer Windows e icona (§3) |
| 15/10 ufficio | `6996efc` | Secondo giro di correzioni (§4) |
| 15/10 ufficio | `c6956ab` | Questo report e `CLAUDE.md` |
| 15/10 19:31 | PR #1 | `feature/installer` → `master` |
| 15/10 casa | `680dcff`, PR #2 | Operatore non più bloccato da un video sul suo monitor (§5) |
| 15/10 casa | `95131aa`, PR #3 | Volume regolabile dal vivo, app che parte massimizzata (§5) |
| 15/10 casa | `3283dfb` | Identifica / Rinomina / Test schermo (§5) |
| 15/10 casa | `4162eec` | Annulla/Ripeti (§5) |
| 16/10 ufficio | merge | Tutti i branch uniti in `master` |
| 16/10 ufficio | `fix/panic-deadlock` | **Blocco dell'app premendo PANIC** con due video a tutto schermo (§5b) |
| 18/10 | PR #9 | Monitor abilita/disabilita, cue Immagine |
| 18/10 | PR #10 | Fix: Stop cue bloccava una catena di Immagini; Annulla non ripristinava Continua |
| 18/10 | PR #12 | Cue con file mancante in rosso, Spazio = GO, nomi monitor salvati nel progetto |
| 18/10 | PR #13 | Barra degli strumenti colorata per funzione, icone per tipo di cue |
| 18/10 | PR #14 | Pannello inspector stile QLab (tab Informazioni/Video/Audio/In-Out) (§5c) |
| 19/10 | PR #15 | Tab In/Out: scrubber di taglio con forma d'onda audio e miniature video (§5c) |
| 19/10 | PR #16 | Countdown che rispetta il taglio, cue Immagine in Monitor Live, zoom/posizione/rotazione dal vivo, cue disattivate sbiadite (§5d) |
| 19/10 | — | Prima release pubblicata su GitHub: `v1.0.0` con l'installer allegato |
| 20/10 | PR #18 | Monitor virtuali "overlay trasparente" per cue Immagine con canale alfa; slider Posizione X/Y fino a ±2000 px (§5e) |
| 20/10 | `fix/schermo-reset-on-add-cue` | Fix: aggiungere una cue azzerava lo "Schermo" di tutte le altre (§5e) |
| 20/10 | `feature/space-go-fix-and-onair-highlight` | Fix: Spazio = GO "mangiato" dal focus; cue in onda evidenziate in ambra (§5e) |
| 20/10 | `feature/cue-type-icon-in-tipo-column` | Icona del tipo di cue nella colonna "Tipo" (§5e) |
| 20/10 | `feature/buymeacoffee-support-dialog` | Finestra di supporto "Buy Me a Coffee" a ogni avvio (§5e) |
| 21/10 ufficio | merge | I quattro branch del 20/10 e il report del 20/10 uniti in `master` |
| 21/10 ufficio | — | Release `v1.1.0` pubblicata su GitHub con l'installer allegato |

---

## 2. Primo giro di correzioni (`ea640e8`)

| # | Problema | Correzione |
|---|---|---|
| 1 | I **monitor virtuali** non venivano salvati nel progetto: dopo la riapertura le cue che li usavano finivano sul monitor principale. | Salvati nel file `.livecue` con il loro Id, così le cue restano collegate. I progetti vecchi senza monitor virtuali continuano ad aprirsi. |
| 2 | Con lo **scaling di Windows** (125%, 150%...) le finestre video uscivano spostate e della misura sbagliata. | App resa *Per-Monitor DPI aware* (`app.manifest`) e finestre di uscita posizionate in pixel reali con `SetWindowPos`, anche passando tra monitor con scaling diverso. |
| 3 | Una cue in **Loop con "Continua"** non passava mai alla successiva. | La successiva parte alla fine del primo giro; la cue in loop continua finché non viene fermata. |
| 3b | Dopo **PANIC/Stop** finestre e player non venivano mai rilasciati (VLC manda `Stopped`, non `EndReached`). | Gestito `Stopped`: pulizia completa anche per le cue già "finite" dal punto di vista della catena. |
| 4 | **GO durante il pre-wait** faceva ripartire la stessa cue. | La selezione passa alla successiva *prima* del pre-wait. |
| 5 | **Percorsi assoluti** dei media: spostando la cartella su un altro PC le cue si rompevano. | Percorsi salvati relativi al file di progetto; per i progetti vecchi, se il percorso assoluto non esiste più, si cerca il file accanto al progetto. |
| 6 | Nessun test e nessun README. | Progetto `tests/LiveCue.Core.Tests` (xUnit) e `README.md`. |
| + | **Bug trovato dai test:** aprendo un progetto salvato si perdevano tutte le impostazioni "Continua"/"Parti insieme". | La regola "l'ultima cue non continua" non viene più applicata durante l'aggiunta di cue (caricamento). |

Refactoring: classe base `MediaCue` (percorso file, volume, loop) per `AudioCue` e `VideoCue`.

---

## 3. Installer (`2f355dd`) e icona (`ab00643`)

**Installer** (`installer/LiveCue.iss` + `installer/build-installer.ps1`):
- Build *self-contained* win-x64: dentro ci sono già .NET 9 e VLC → sui PC di destinazione **non serve installare niente**.
- Windows 10/11 a 64 bit (anche ARM64 in emulazione). Non funziona su Windows a 32 bit.
- Installazione "per tutti gli utenti" (admin) o "solo per me" (senza admin), italiano/inglese, menu Start, icona desktop opzionale, disinstallazione da Pannello di controllo.
- Solo la versione x64 di VLC viene inclusa (setup da 64 MB invece di ~110).
- L'eseguibile si chiama `LiveCue.exe`, versione 1.0.0.
- **Testato:** installazione silenziosa, avvio, disinstallazione.

**Icona** (`src/LiveCue.App/Assets/LiveCue.ico`, generata da `tools/make_icon.py`): riquadro scuro, lista di cue con la prossima in ambra, triangolo verde del GO. Versione semplificata per 16–32 px. Usata da exe, finestre e installer.

---

## 4. Secondo giro di correzioni (`6996efc`)

| # | Problema | Correzione |
|---|---|---|
| 1 | **L'interfaccia si bloccava ~0,5 s ogni 5 s** (lettura GPU sul thread dell'interfaccia; misurati 577 ms). GO poteva partire in ritardo. | Lettura CPU/RAM/GPU spostata su un thread in background. |
| 2 | Un **percorso file non valido** (o un progetto danneggiato) **faceva chiudere l'app**. | Gestore globale degli errori: l'errore va nella barra di stato e in `%LOCALAPPDATA%\LiveCue\errors.log`, l'app resta aperta. Errori di apertura/salvataggio mostrati con un messaggio; un file danneggiato non tocca il progetto aperto. |
| 3 | Un **file mancante** lasciava la finestra nera sul proiettore e bloccava le catene "Continua". | Controllo del file prima di riprodurre + gestione di `EncounteredError` di VLC: la cue viene saltata con un avviso e la catena prosegue. All'apertura del progetto viene segnalato quanti file mancano. |
| 4 | Se il **proiettore si scollegava**, il video andava a tutto schermo sul monitor dell'operatore. | Se lo schermo della cue non c'è, si usa il primo monitor secondario (con avviso); se non ce n'è nessuno la cue **non** viene proiettata sul monitor principale. |
| 5 | **"5" nel pre-wait = 5 giorni.** | Pre-wait/post-wait si scrivono in **secondi** ("2,5") oppure `m:ss`; input non valido rifiutato. Colonne "Pre-wait (s)" / "Post-wait (s)". |
| 6 | **Numeri delle cue duplicati** dopo un'eliminazione. | Nuovo numero = numero più alto + 1. |
| 7 | **Modifiche perse senza avviso** con Nuovo/Apri/chiusura. | Tracciamento modifiche (asterisco `*` nel titolo) e richiesta "Vuoi salvare?". |
| 8 | Una **cue disattivata bloccava il GO**, e non si poteva riattivare. | GO e catene saltano le cue disattivate; nuova colonna **"Attiva"**. |

---

## 5. Lavoro della sera del 15/10 (da casa, sessione cloud)

### Già in `master`

- **Operatore non più bloccato da un video sul proprio monitor** (`680dcff`, PR #2). La superficie video nativa di VLC "rubava" il focus della tastiera: con un video a tutto schermo sul monitor dell'app non si poteva più cliccare né usare ESC. Ora le finestre di uscita non possono mai prendere il focus (`WS_EX_NOACTIVATE` a livello Win32) ed **ESC = PANIC è un tasto globale di sistema** (`RegisterHotKey`): funziona qualunque finestra abbia il focus. *Verificato dal vivo.*
- **Volume regolabile dal vivo** (`95131aa`, PR #3): colonna Volume con cursore (0–1) nella lista; spostandolo mentre la cue suona il volume cambia subito. L'app **parte massimizzata**.

### Uniti in `master` il 16/10

- **Identifica / Rinomina / Test schermo** (`feature/monitor-identify-rename-screentest`, `3283dfb`):
  - *Identifica monitor*: mostra per qualche secondo il numero "Monitor N" di LiveCue su ogni schermo. Serve perché la numerazione di Windows (Impostazioni schermo) può **non coincidere** con quella di LiveCue — confermato su una macchina reale.
  - *Rinomina*: nome personalizzato per ogni monitor al posto di "Generic PnP Monitor", mostrato ovunque. **Attenzione: vale solo per la sessione aperta, non viene salvato nel progetto.**
  - *Test schermo*: immagine di prova a tutto schermo per monitor (barre colore, griglia di allineamento, cerchi, marcatori ai bordi, nome e risoluzione). Clic o ESC per chiudere.
  - I due aiuti Win32 (posizionamento in pixel, `WS_EX_NOACTIVATE`) sono raccolti in `Win32WindowPlacement` e riusati dalle nuove finestre.
  - *Verificato dal vivo su 3 monitor con scaling misto.*
- **Annulla/Ripeti** (`feature/undo-redo`, `4162eec`): Ctrl+Z / Ctrl+Y e pulsanti con descrizione dell'azione. Copre aggiunta/eliminazione/riordino delle cue e creazione/modifica/rimozione dei monitor virtuali. **Non copre** le modifiche ai singoli campi nella griglia (nome, tempi, continua...): per quelle c'è solo l'ESC della griglia durante la modifica. `UndoManager` è in Core con i suoi test.

---

## 5b. Blocco dell'app premendo PANIC (16/10)

**Sintomo** (segnalato in ufficio): due filmati diversi su due monitor, PANIC (ESC) → schermi neri, poi bianchi, poi Windows propone di chiudere l'app. Il registro eventi di Windows conferma un *Application Hang* (`AppHangB1`), non un errore.

**Causa:** `MediaPlayer.Stop()` e `Dispose()` di LibVLC sono bloccanti — aspettano che VLC chiuda la sua uscita video — e venivano chiamati sul thread dell'interfaccia. Ma VLC, per chiudere l'uscita video, manda messaggi alla finestra video, che appartiene proprio al thread dell'interfaccia → **deadlock**. Lo stesso rischio c'era quando un video finiva da solo.

**Correzione** (`MediaPlayerCuePlayer`): Stop e Dispose girano sempre su un thread separato (`StopOffUiThreadAsync`, mai due volte sullo stesso player); PANIC nasconde subito le finestre, che vengono chiuse solo dopo che VLC ha davvero finito.

**Verifica** con un programma di prova che usa il vero player di LiveCue (due video di prova generati con ffmpeg, a tutto schermo sui due monitor dell'ufficio, 1920x1080 ciascuno):
- *prima della correzione:* app bloccata già al primo PANIC (riprodotto);
- *dopo:* 30 PANIC di fila senza blocchi (la chiamata PANIC dura al massimo 5 ms) e 10 fine-video naturali, tutte le finestre chiuse ogni volta.

Il programma di prova non è nel repository (era in una cartella temporanea): se serve rifarlo, la ricetta è "due `VideoCue` a tutto schermo su monitor diversi → `PlayAsync` → dopo ~1,5 s `cts.Cancel()` + `StopAll()` sul thread UI, con un thread di controllo che verifica che il thread UI risponda".

---

## 5c. Pannello inspector e tab In/Out (18-19/10, PR #14 e #15)

Nuovo pannello in basso nella finestra principale, stile QLab, con 4 tab per la cue selezionata:

- **Informazioni**: dati del file (dimensione, durata, risoluzione, bitrate, codec, canali audio).
- **Video**: monitor di destinazione, correzione colore dal vivo (luminosità/contrasto/saturazione, solo cue video), e da PR #16 anche zoom/posizione/rotazione (vedi §5d).
- **Audio**: scelta del dispositivo di uscita audio.
- **In/Out**: scrubber grafico per tagliare inizio/fine del file, con **forma d'onda audio** e **miniature video** generate dal vivo (analisi in background su thread separato, con cache su disco per non rianalizzare lo stesso file più volte).

La forma d'onda viene letta dai callback audio nativi di LibVLC (`SetAudioFormatCallback`/`SetAudioCallbacks`), le miniature con `TakeSnapshot` durante una riproduzione normale (snapshot opportunistico quando `TimeChanged` attraversa ogni istante target — l'approccio "pausa, cerca, scatta" si è rivelato inaffidabile). Qui sono stati **trovati e corretti due crash reali** (`AccessViolationException`) durante lo sviluppo: non scrivere mai nel parametro nativo `format` dei callback audio, e tenere in vita i delegate passati a LibVLC con variabili tipizzate esplicite + `GC.KeepAlive`.

*Verificato dal vivo con un video reale scaricato da YouTube (transcodificato in H.264/AAC).*

## 5d. Countdown, cue Immagine in Monitor Live, zoom/posizione/rotazione, cue disattivate sbiadite (19/10, PR #16)

Quattro richieste indipendenti, tutte verificate dal vivo:

- **Countdown rispetta il taglio**: prima contava sempre fino alla fine del file intero; ora conta fino a `TrimEnd` (o alla fine vera se non tagliato).
- **Cue Immagine in Monitor Live**: anche le cue immagine ora compaiono nell'anteprima "Monitor Live" (prima solo le cue video). Gli eventi `OutputWindowOpened`/`OutputWindowClosed` sono stati generalizzati per funzionare con entrambi i tipi di cue.
- **Zoom, posizione X/Y, rotazione dal vivo**: nuovi controlli nel tab Video, applicati mentre la cue è in riproduzione. Scelto un `RenderTransform` WPF (non un filtro LibVLC) per non aggiungere altro rischio di interop nativa, e perché funziona identico per video e immagini.
  - **Limite noto**: la **rotazione non ha alcun effetto sulle cue video** — la superficie video di LibVLC è una finestra Win32 nativa annidata in WPF, che si può ridimensionare/spostare (zoom e posizione funzionano) ma non ruotare (limite "airspace" di WPF: un hwnd figlio nativo non supporta la rotazione via `RenderTransform`). Lo slider Rotazione è **disabilitato automaticamente** per le cue video con una nota in UI; su cue immagine funziona normalmente (nessun hwnd nativo, solo un controllo `Image` WPF).
- **Cue disattivate più sbiadite**: righe con "Attiva" deselezionato a opacità 0.35, per far risaltare le cue armate in una lista lunga.

---

## 5e. Overlay trasparenti, fix Schermo e Spazio, icone, Buy Me a Coffee (20/10, uniti il 21/10)

- **Monitor virtuali "overlay trasparente"** (PR #18): un monitor virtuale può essere segnato come *overlay trasparente*; una cue **Immagine** con canale alfa (logo PNG, sottopancia...) mostra quello che c'è dietro — desktop o il video di un'altra cue sullo stesso monitor — invece di coprirlo. `OverlayZOrderTracker` tiene l'overlay sempre sopra le altre uscite sullo stesso monitor, in qualsiasi ordine vengano aperte.
  - **Limite voluto**: le cue **video** su un overlay restano **opache** — la superficie video di LibVLC è una finestra nativa e una finestra WPF trasparente non la comporrebbe (il video sparirebbe).
  - Se l'uscita ripiega su un monitor secondario (schermo mancante), la trasparenza non viene applicata.
  - *Verificato dal vivo* con un video H.264 e un PNG semitrasparente, in entrambi gli ordini di apertura.
  - Slider **Posizione X/Y** allargati a ±2000 px (prima ±600 non arrivavano ai bordi di uno schermo 4K).
- **Fix "Schermo" azzerato** (`fix/schermo-reset-on-add-cue`): aggiungendo qualsiasi cue, la colonna "Schermo" di **tutte le altre cue** si svuotava. Causa: l'elenco dei monitor veniva ricostruito con `Clear()` + aggiunta, e durante quel "reset" i menu a tendina WPF scrivevano `null` nel valore collegato. Ora `DisplayManager.SyncDisplayList` aggiorna l'elenco sul posto (toglie solo i monitor spariti, aggiunge solo i nuovi), con test dedicati.
- **Fix Spazio = GO** (`feature/space-go-fix-and-onair-highlight`): se il focus era rimasto su una casella (Loop/Attiva), un menu o un pulsante, la barra spaziatrice non faceva partire la cue. Ora Spazio fa sempre GO, tranne quando si sta scrivendo in un campo di testo o modificando una cella. *Verificato dal vivo.*
  - **Cue in onda in ambra**: la riga di una cue che sta suonando/mostrando è evidenziata nell'ambra del logo (nuova proprietà `Cue.IsPlaying`, non salvata). *Verificato dal vivo.*
- **Icona del tipo nella colonna "Tipo"** (`feature/cue-type-icon-in-tipo-column`): stesso simbolo del pulsante "Aggiungi cue" della barra.
- **Finestra "Buy Me a Coffee"** (`feature/buymeacoffee-support-dialog`): **a ogni avvio** compare una finestra modale con QR code e link a buymeacoffee.com/cin8; "No, grazie" si attiva dopo 3 secondi. È modale di proposito: una versione non modale finiva nascosta dietro la finestra principale senza modo di recuperarla.

---

## 6. Cosa NON è stato verificato a mano

Verificati dal vivo (sera del 15/10): blocco dell'operatore + ESC globale; Identifica/Rinomina/Test schermo su 3 monitor con scaling misto. Verificato con programma di prova (16/10): PANIC e fine naturale con due video a tutto schermo su due monitor (§5b).

Ancora da provare dal vivo:
- cue con file mancante, file corrotto, proiettore scollegato durante lo spettacolo;
- loop + "Continua";
- richiesta di salvataggio alla chiusura / Nuovo / Apri;
- Annulla/Ripeti;
- fix "Schermo" azzerato (§5e): aggiungere cue a una lista che ha già schermi assegnati e controllare che restino;
- finestra Buy Me a Coffee all'avvio (§5e);
- installazione del setup su un **altro** PC.

---

## 7. Prossimi passi suggeriti

1. **Provare dal vivo** gli scenari del §6.
2. **Finestra Buy Me a Coffee**: oggi compare a **ogni** avvio con 3 secondi di attesa forzata. In un'app da spettacolo può dare fastidio (es. riavvio di emergenza durante uno show). Da valutare: mostrarla una volta ogni tanto, o mai dopo la prima chiusura, o renderla disattivabile.
3. **Annulla/Ripeti anche per le modifiche ai campi** della griglia (oggi copre solo aggiunta/eliminazione/riordino cue e monitor virtuali).
4. **Doppio clic su `.livecue`** per aprirlo (associazione file nell'installer + apertura da riga di comando nell'app).
5. **Firma digitale del setup** per togliere l'avviso SmartScreen sugli altri PC (serve un certificato di firma del codice, a pagamento).
6. **Rotazione per le cue video**: oggi funziona solo su immagine (vedi §5d) per il limite "airspace" di WPF/LibVLC. Per supportarla servirebbe renderizzare il frame video su una superficie gestita da WPF invece che sull'hwnd nativo di LibVLC (es. callback di render software/D3D) — lavoro di interop nativo non banale, da valutare solo se la rotazione sui video diventa davvero necessaria.
7. **Pulizia branch remoti** già uniti ma non cancellati (lista in §1).
8. **Export automatico dell'installer su ogni release**: oggi la build e la pubblicazione su GitHub Releases sono manuali (vedi §8); si potrebbe automatizzare con una GitHub Action quando si taggano le versioni.

~~Colonna Volume~~ — fatta (PR #3). ~~Salvare i nomi dei monitor~~ — fatta (PR #12). ~~Barra spaziatrice = GO~~ — fatta (PR #12). ~~Cue con file mancante in rosso~~ — fatta (PR #12). ~~Fade in/out~~ — fatta.

---

## 8. Come riprendere da un altro PC

1. Installa gli strumenti (una volta sola):
   ```
   winget install Git.Git
   winget install Microsoft.DotNet.SDK.9
   winget install JRSoftware.InnoSetup
   ```
   (Per rigenerare l'icona serve anche Python con Pillow: `pip install pillow`.)
2. Scarica il progetto (o, se c'è già, aggiornalo con `git switch master` + `git pull`):
   ```
   git clone https://github.com/ncrdavide-svg/LiveCue.git
   cd LiveCue
   ```
3. Comandi utili:
   ```
   dotnet build LiveCue.sln
   dotnet test
   dotnet run --project src/LiveCue.App
   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0
   ```
4. Apri Claude Code nella cartella `LiveCue`: legge da solo `CLAUDE.md` (istruzioni tecniche); chiedigli di leggere questo file per sapere dove eravamo rimasti — e, a fine lavoro, di aggiornarlo e fare il push.
5. In alternativa a ricompilare l'installer, si può scaricare l'ultima versione già pronta dalla pagina [Releases](https://github.com/ncrdavide-svg/LiveCue/releases) su GitHub.

**PC dell'ufficio:** progetto in `Desktop\livecue\LiveCue`.
