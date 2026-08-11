namespace YuruChara.Domain.Mascots;

/// <summary>
/// How much confidence a mascot record carries.
/// <para>
/// This is deliberately a separate field from whether a prefecture has a mascot
/// at all. Coverage ("do we have a row?") and confidence ("do we believe it?")
/// are different questions, and collapsing them into one flag loses the ability
/// to say "we have 40 mascots, we have personally checked 10 of them". The UI
/// shows the distinction rather than hiding it.
/// </para>
/// </summary>
public enum VerificationLevel
{
    /// <summary>Taken from an automated pass (Wikidata SPARQL) and not hand-checked.</summary>
    Automated = 0,

    /// <summary>Checked field by field against the owning body's own website.</summary>
    ManuallyVerified = 1
}
