# LiveCue — Report del lavoro e stato del progetto

*Ultimo aggiornamento: 15 ottobre 2026*

Questo documento riassume tutto il lavoro fatto su LiveCue con Claude Code, il punto in cui siamo arrivati e come riprendere da un altro PC.

---

## 1. Dove siamo (in breve)

- **Branch di lavoro:** `feature/installer` — contiene **tutto** (è già su GitHub).
- **`master`** è ancora fermo al commit iniziale: **nessuna pull request è stata aperta**. Per portare tutto in `master` basta una sola PR da `feature/installer`:
  https://github.com/ncrdavide-svg/LiveCue/pull/new/feature/installer
- `fix/review-issues` è un branch intermedio, già incluso in `feature/installer`: dopo il merge si può cancellare.
- **Build:** pulita, nessun warning. **Test:** 38 su 38 superati.
- **Setup:** `LiveCue-Setup-1.0.0.exe` (64 MB) si genera con lo script dell'installer (vedi §6). Non è nel repository (la cartella `artifacts/` è esclusa da Git): va rigenerato su ogni PC.

### Storia dei commit (`feature/installer`)

| Commit | Contenuto |
|---|---|
| `9772f6d` | Commit iniziale (codice originale) |
| `ea640e8` | Primo giro di correzioni + test + README |
| `2f355dd` | Installer Windows (Inno Setup) |
| `ab00643` | Icona dell'app |
| `6996efc` | Secondo giro di correzioni |
| *(questo)* | Report e istruzioni (`docs/STATO-PROGETTO.md`, `CLAUDE.md`) |

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
- L'eseguibile si chiama ora `LiveCue.exe`, versione 1.0.0.
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

## 5. Cosa NON è stato verificato a mano

I test automatici coprono la logica (`LiveCue.Core`); l'app è stata avviata e chiusa senza errori, ma questi scenari vanno provati dal vivo:

- riproduzione di video/audio reali su più monitor e con scaling diverso dal 100%;
- cue con file mancante, file corrotto, proiettore scollegato;
- loop + "Continua";
- richiesta di salvataggio alla chiusura;
- installazione del setup su un **altro** PC.

---

## 6. Come riprendere da un altro PC

1. Installa gli strumenti (una volta sola):
   ```
   winget install Git.Git
   winget install Microsoft.DotNet.SDK.9
   winget install JRSoftware.InnoSetup
   ```
   (Per rigenerare l'icona serve anche Python con Pillow: `pip install pillow`.)
2. Scarica il progetto e passa al branch di lavoro:
   ```
   git clone https://github.com/ncrdavide-svg/LiveCue.git
   cd LiveCue
   git switch feature/installer
   ```
3. Comandi utili:
   ```
   dotnet build LiveCue.sln
   dotnet test
   dotnet run --project src/LiveCue.App
   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0
   ```
4. Apri Claude Code nella cartella `LiveCue`: legge da solo `CLAUDE.md` (istruzioni tecniche) e puoi chiedergli di leggere questo file per sapere dove eravamo rimasti.

---

## 7. Prossimi passi suggeriti

Da decidere/fare, in ordine di utilità:

1. **Aprire e unire la pull request** `feature/installer` → `master`.
2. **Provare dal vivo** gli scenari del §5.
3. **Barra spaziatrice = GO** (come QLab); ora c'è solo ESC = PANIC.
4. **Colonna Volume** nella griglia (il valore esiste già nel modello).
5. **Cue con file mancante evidenziate in rosso** nella lista.
6. **Fade in/out** e cue "Stop"/"Fade" (i tipi `Stop`, `Wait`, `Group` sono già previsti in `CueType` ma non implementati).
7. **Doppio clic su `.livecue`** per aprirlo (associazione file nell'installer + apertura da riga di comando nell'app).
8. **Firma digitale del setup** per togliere l'avviso SmartScreen sugli altri PC (serve un certificato di firma del codice, a pagamento).
