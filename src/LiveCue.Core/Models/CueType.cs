namespace LiveCue.Core.Models;

public enum CueType
{
    Video,
    Audio,
    Group,
    Stop,
    Wait,
}

public enum ContinueMode
{
    /// <summary>Non continua: finita la cue, l'esecuzione si ferma (scelta di default).</summary>
    DoNotContinue,

    /// <summary>Continua: finita questa cue, parte automaticamente quella successiva.</summary>
    Continue,

    /// <summary>Parti insieme: la cue successiva parte in contemporanea a questa.</summary>
    StartTogether,
}
