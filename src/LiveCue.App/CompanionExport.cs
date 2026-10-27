using System.Text;
using LiveCue.Core.Models;

namespace LiveCue.App;

// Genera una pagina di Bitfocus Companion (.companionconfig) con i tasti di LiveCue.
// Formato "page" versione 9: Companion lo aggiorna da solo all'importazione.
// Ogni tasto usa il modulo "Generic HTTP" per chiamare l'API di LiveCue (RemoteApi).
// Stesso schema di LiveCount/CompanionExport.cs, adattato ai comandi di LiveCue.
public static class CompanionExport
{
    private const string ConnectionId = "livecue_http";

    // Stessa palette di LiveCue/LiveCount (RGB come intero, come lo salva Companion).
    private const int White = 0xFFFFFF, Ink = 0x0D0C22;
    private const int Green = 0x22A046, Red = 0xC62828, Grey = 0x626A7A;
    private const int CueBg = 0x2E3440, DisarmedBg = 0x44484F;

    private sealed class Key
    {
        public string Text = "";
        public string Url = "";
        public int Bg;
        public int Fg = White;
    }

    /// <summary>columns x rows: dimensione del deck (5x3 = Stream Deck classico, 8x4 = XL).</summary>
    public static string Build(IReadOnlyList<Cue> cues, string baseUrl, int columns, int rows, out int hiddenCues)
    {
        var grid = new Dictionary<int, Dictionary<int, Key>>();
        void Put(int r, int c, Key k)
        {
            if (!grid.TryGetValue(r, out var row)) grid[r] = row = new Dictionary<int, Key>();
            row[c] = k;
        }

        // Prima riga: trasporto e navigazione della selezione.
        Put(0, 0, new Key { Text = "GO", Url = "go", Bg = Green });
        Put(0, 1, new Key { Text = "PANIC", Url = "panic", Bg = Red });
        Put(0, 2, new Key { Text = "↑ SU", Url = "prev", Bg = Grey });
        Put(0, 3, new Key { Text = "↓ GIÙ", Url = "next", Bg = Grey });

        // Le cue riempiono i tasti liberi riga per riga, poi proseguono sotto.
        var free = new List<(int Row, int Col)>();
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
                if (!(grid.TryGetValue(r, out var row) && row.ContainsKey(c)))
                    free.Add((r, c));

        int extraRow = rows, extraCol = 0;
        hiddenCues = 0;
        foreach (var cue in cues)
        {
            int r, c;
            if (free.Count > 0) { (r, c) = free[0]; free.RemoveAt(0); }
            else
            {
                r = extraRow; c = extraCol; hiddenCues++;
                if (++extraCol >= columns) { extraCol = 0; extraRow++; }
            }
            Put(r, c, new Key
            {
                Text = Escape(cue.Number) + "\\n" + Escape(Truncate(cue.Name, 16)),
                Url = "cue?number=" + Uri.EscapeDataString(cue.Number),
                Bg = cue.IsArmed ? CueBg : DisarmedBg,
                Fg = cue.IsArmed ? White : 0xAAAAAA,
            });
        }
        var maxRow = Math.Max(rows - 1, extraCol > 0 ? extraRow : extraRow - 1);

        var sb = new StringBuilder();
        sb.Append("{\n\"version\": 9,\n\"type\": \"page\",\n\"companionBuild\": \"LiveCue\",\n");
        sb.Append("\"page\": {\n\"id\": \"livecue\",\n\"name\": \"LiveCue\",\n\"controls\": {");
        var firstRow = true;
        foreach (var row in grid)
        {
            sb.Append(firstRow ? "\n" : ",\n").Append('"').Append(row.Key).Append("\": {");
            firstRow = false;
            var firstCol = true;
            foreach (var col in row.Value)
            {
                sb.Append(firstCol ? "\n" : ",\n").Append('"').Append(col.Key).Append("\": ");
                firstCol = false;
                AppendButton(sb, col.Value);
            }
            sb.Append("\n}");
        }
        sb.Append("\n},\n\"gridSize\": {\"minColumn\": 0, \"maxColumn\": ").Append(Math.Max(7, columns - 1))
          .Append(", \"minRow\": 0, \"maxRow\": ").Append(Math.Max(3, maxRow)).Append("}\n},\n");

        sb.Append("\"instances\": {\n\"").Append(ConnectionId).Append("\": {");
        sb.Append("\"label\": \"LiveCue\", \"moduleId\": \"generic-http\", \"enabled\": true, \"sortOrder\": 0, ");
        sb.Append("\"isFirstInit\": false, \"lastUpgradeIndex\": 3, ");
        sb.Append("\"config\": {\"prefix\": ").Append(RemoteApi.JsonString(baseUrl))
          .Append(", \"proxyAddress\": \"\", \"authType\": \"none\", \"authUser\": \"\", \"rejectUnauthorized\": true, \"insecureHTTPParser\": false}");
        sb.Append("}\n},\n\"connectionCollections\": [],\n\"oldPageNumber\": 1\n}\n");
        return sb.ToString();
    }

    private static void AppendButton(StringBuilder sb, Key k)
    {
        sb.Append("{\"type\": \"button\", \"style\": {\"text\": \"").Append(k.Text)
          .Append("\", \"textExpression\": false, \"size\": \"auto\", \"png64\": null, \"alignment\": \"center:center\", \"pngalignment\": \"center:center\", ")
          .Append("\"color\": ").Append(k.Fg).Append(", \"bgcolor\": ").Append(k.Bg).Append(", \"show_topbar\": \"default\"}, ");
        sb.Append("\"options\": {\"stepProgression\": \"auto\", \"stepExpression\": \"\", \"rotaryActions\": false}, ");
        sb.Append("\"feedbacks\": [], ");
        sb.Append("\"steps\": {\"0\": {\"action_sets\": {\"down\": [{\"id\": \"").Append(NewId())
          .Append("\", \"definitionId\": \"get\", \"connectionId\": \"").Append(ConnectionId)
          .Append("\", \"type\": \"action\", \"upgradeIndex\": 3, \"children\": {}, \"options\": {")
          // Companion 5 salva le opzioni dei moduli come { isExpression, value }.
          .Append("\"url\": ").Append(Opt(RemoteApi.JsonString(k.Url)))
          .Append(", \"header\": ").Append(Opt("\"\""))
          .Append(", \"jsonResultDataVariable\": ").Append(Opt("\"\""))
          .Append(", \"result_stringify\": ").Append(Opt("true"))
          .Append(", \"statusCodeVariable\": ").Append(Opt("\"\""))
          .Append("}}], \"up\": []}, ")
          .Append("\"options\": {\"runWhileHeld\": []}}}, ");
        sb.Append("\"localVariables\": []}");
    }

    private static string Opt(string jsonValue) => "{\"isExpression\": false, \"value\": " + jsonValue + "}";

    /// <summary>Testo già pronto per l'interno di una stringa JSON.</summary>
    private static string Escape(string s)
    {
        var j = RemoteApi.JsonString(s);
        return j[1..^1];
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string NewId() => Guid.NewGuid().ToString("N")[..21];
}
