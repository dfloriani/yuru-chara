using System.Text.Json.Serialization;

namespace YuruChara.Ingestion.Wikidata;

/// <summary>
/// Just enough of the SPARQL 1.1 Query Results JSON Format to read a SELECT
/// response. <see href="https://www.w3.org/TR/sparql11-results-json/"/>.
/// <para>
/// Hand-written rather than pulled in with a SPARQL client library. The format is
/// three nested objects and this project sends exactly one query, so a dependency
/// would be more code to explain than it saves. It is also the layer where the
/// response is at its most literal, which is useful when the question is "what did
/// Wikidata actually say".
/// </para>
/// </summary>
public sealed record SparqlResponse
{
    [JsonPropertyName("head")]
    public SparqlHead Head { get; init; } = new();

    [JsonPropertyName("results")]
    public SparqlResults Results { get; init; } = new();
}

public sealed record SparqlHead
{
    /// <summary>Column names, in the order the query declared them.</summary>
    [JsonPropertyName("vars")]
    public IReadOnlyList<string> Vars { get; init; } = [];
}

public sealed record SparqlResults
{
    /// <summary>
    /// One dictionary per row. A variable that is unbound in a row is <em>absent</em>
    /// from that row's dictionary rather than present with a null — which is why
    /// every read goes through <see cref="SparqlRowExtensions.Value"/> instead of an
    /// indexer.
    /// </summary>
    [JsonPropertyName("bindings")]
    public IReadOnlyList<Dictionary<string, SparqlValue>> Bindings { get; init; } = [];
}

/// <param name="Type">"uri", "literal" or "bnode".</param>
/// <param name="Value">The value as text. Always text, even for numbers and dates.</param>
/// <param name="Datatype">XSD datatype URI, when the value is a typed literal.</param>
public sealed record SparqlValue(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("datatype")] string? Datatype = null);

public static class SparqlRowExtensions
{
    /// <summary>
    /// Reads a variable from a row, or null if it is unbound in that row.
    /// <para>
    /// GROUP_CONCAT is why the empty string is folded into null as well: an
    /// aggregate over nothing returns a bound empty string rather than an unbound
    /// variable, so "no websites at all" arrives looking different from "no
    /// inception date at all" unless they are normalised here.
    /// </para>
    /// </summary>
    public static string? Value(this Dictionary<string, SparqlValue> row, string name) =>
        row.TryGetValue(name, out var value) && value.Value.Length > 0 ? value.Value : null;

    /// <summary>
    /// Reads a variable that must be present, and says which one was missing if it
    /// is not. Used for the columns the query guarantees, so a change to the query
    /// that drops a column fails with a readable message.
    /// </summary>
    public static string Required(this Dictionary<string, SparqlValue> row, string name) =>
        row.Value(name)
        ?? throw new InvalidOperationException(
            $"SPARQL result row is missing required variable '{name}'. " +
            "The query and the code that reads it have diverged.");

    /// <summary>
    /// Splits a GROUP_CONCAT value back into its parts, dropping empties.
    /// </summary>
    public static IReadOnlyList<string> Concatenated(
        this Dictionary<string, SparqlValue> row,
        string name,
        string separator) =>
        row.Value(name) is { } joined
            ? joined.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];
}
