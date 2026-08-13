using Microsoft.EntityFrameworkCore;
using YuruChara.Infrastructure;

namespace YuruChara.Api.Mascots;

/// <summary>
/// Everything under <c>/api/mascots</c>. One static class per <c>MapGroup</c>, per
/// the CLAUDE.md convention.
/// </summary>
public static class MascotEndpoints
{
    public static RouteGroupBuilder MapMascotEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/mascots");

        group.MapGet("/", GetMascotsAsync).WithName("GetMascots");
        group.MapGet("/{id:guid}", GetMascotAsync).WithName("GetMascot");

        return group;
    }

    /// <summary>
    /// <c>GET /api/mascots?motif=&amp;debutBefore=</c> — the flat list behind the
    /// searchable list view, which CLAUDE.md treats as a first-class interface on a
    /// phone rather than a fallback for the map.
    /// </summary>
    /// <param name="motif">
    /// Case-insensitive substring match. Motif is deliberately free text — the real
    /// values run from "pear" to "samurai helmet" — so an exact match would find
    /// almost nothing.
    /// </param>
    /// <param name="debutBefore">
    /// Exclusive. Mascots with no recorded debut year are excluded when this filter
    /// is applied, because "unknown" is not "before 2010"; with no filter they are
    /// included. Treating an unknown year as passing the filter is the other
    /// available reading, and this is the one that never asserts something
    /// unverified.
    /// </param>
    private static async Task<IResult> GetMascotsAsync(
        YuruCharaDbContext db,
        string? motif,
        int? debutBefore,
        CancellationToken cancellationToken)
    {
        // Not output-cached, unlike the boundaries endpoint. The response is a few
        // kilobytes, and the two filters open up a much larger key space than the
        // three variants of /api/prefectures — the cache would hold many entries and
        // save little. See DECISIONS.md 14.
        var query = db.Mascots.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(motif))
        {
            // EF.Functions.ILike translates to PostgreSQL's ILIKE, which is
            // case-insensitive without the ToLower() on both sides that would defeat
            // any index. Npgsql has no translation for
            // string.Contains(value, StringComparison.OrdinalIgnoreCase), so this is
            // the readable way to say it.
            //
            // The caller's text is escaped before the wildcards are wrapped around it,
            // so a motif containing % or _ is matched literally instead of being read
            // as a pattern.
            var pattern = $"%{EscapeLikePattern(motif.Trim())}%";
            query = query.Where(mascot =>
                mascot.Motif != null && EF.Functions.ILike(mascot.Motif, pattern, @"\"));
        }

        if (debutBefore is { } year)
        {
            query = query.Where(mascot => mascot.DebutYear != null && mascot.DebutYear < year);
        }

        var rows = await query
            .OrderBy(mascot => mascot.PrefectureId)
            .ThenByDescending(mascot => mascot.IsOfficial)
            .ThenBy(mascot => mascot.NameJa)
            .Select(mascot => new MascotRow(mascot, mascot.Prefecture!.NameEn))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows.Select(row => row.ToResponse()).ToList());
    }

    /// <summary>
    /// <c>GET /api/mascots/{id}</c>. The Guid comes from the committed seed file
    /// rather than being generated, so these URLs survive a re-seed.
    /// </summary>
    private static async Task<IResult> GetMascotAsync(
        YuruCharaDbContext db,
        Guid id,
        CancellationToken cancellationToken)
    {
        var row = await db.Mascots
            .AsNoTracking()
            .Where(mascot => mascot.Id == id)
            .Select(mascot => new MascotRow(mascot, mascot.Prefecture!.NameEn))
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(row.ToResponse());
    }

    /// <summary>
    /// Escapes the three characters that are special inside a SQL LIKE pattern, with
    /// a backslash — which is what the third argument to <c>EF.Functions.ILike</c>
    /// above declares as the escape character.
    /// </summary>
    private static string EscapeLikePattern(string value) =>
        value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
