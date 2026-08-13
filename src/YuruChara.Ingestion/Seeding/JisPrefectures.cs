using YuruChara.Domain.Prefectures;

namespace YuruChara.Ingestion.Seeding;

/// <summary>
/// The 47 prefectures: JIS X 0401 code, names, and region.
/// <para>
/// This is the reference table the whole seed pipeline joins on. It is code rather
/// than a data file because it is a closed, standardised set that has not changed
/// since 1972 and cannot change without an act of the Diet — there is nothing to
/// re-ingest. Putting it in code also means the compiler and the tests can check
/// it, which a JSON file would not.
/// </para>
/// <para>
/// <b>Provenance.</b> Codes and Japanese and English names are as returned by the
/// Wikidata query in <c>Wikidata/prefecture-mascots.rq</c>'s sibling prefecture
/// query (P300 / rdfs:label), retrieved 2026-08-13, and the Japanese names match
/// the <c>N03_001</c> values in <c>data/prefectures.geojson</c> character for
/// character. See DATA-SOURCES.md.
/// </para>
/// <para>
/// <b>The regions are not from Wikidata.</b> Prefecture items carry no queryable
/// statement pointing at a region item, so the query returns nothing for it. The
/// eight-region grouping below is the conventional one listed in CLAUDE.md. It is
/// a standard classification rather than a researched fact, and it is not a fact
/// about a mascot, so writing it out here does not conflict with "do not invent
/// data".
/// </para>
/// </summary>
public static class JisPrefectures
{
    /// <summary>
    /// Every prefecture, ordered by JIS code.
    /// </summary>
    public static readonly IReadOnlyList<JisPrefecture> All =
    [
        new(1,  "北海道",   "Hokkaido",  "Hokkaidō",  Region.Hokkaido),
        new(2,  "青森県",   "Aomori",    "Aomori",    Region.Tohoku),
        new(3,  "岩手県",   "Iwate",     "Iwate",     Region.Tohoku),
        new(4,  "宮城県",   "Miyagi",    "Miyagi",    Region.Tohoku),
        new(5,  "秋田県",   "Akita",     "Akita",     Region.Tohoku),
        new(6,  "山形県",   "Yamagata",  "Yamagata",  Region.Tohoku),
        new(7,  "福島県",   "Fukushima", "Fukushima", Region.Tohoku),
        new(8,  "茨城県",   "Ibaraki",   "Ibaraki",   Region.Kanto),
        new(9,  "栃木県",   "Tochigi",   "Tochigi",   Region.Kanto),
        new(10, "群馬県",   "Gunma",     "Gunma",     Region.Kanto),
        new(11, "埼玉県",   "Saitama",   "Saitama",   Region.Kanto),
        new(12, "千葉県",   "Chiba",     "Chiba",     Region.Kanto),
        new(13, "東京都",   "Tokyo",     "Tōkyō",     Region.Kanto),
        new(14, "神奈川県", "Kanagawa",  "Kanagawa",  Region.Kanto),
        new(15, "新潟県",   "Niigata",   "Niigata",   Region.Chubu),
        new(16, "富山県",   "Toyama",    "Toyama",    Region.Chubu),
        new(17, "石川県",   "Ishikawa",  "Ishikawa",  Region.Chubu),
        new(18, "福井県",   "Fukui",     "Fukui",     Region.Chubu),
        new(19, "山梨県",   "Yamanashi", "Yamanashi", Region.Chubu),
        new(20, "長野県",   "Nagano",    "Nagano",    Region.Chubu),
        new(21, "岐阜県",   "Gifu",      "Gifu",      Region.Chubu),
        new(22, "静岡県",   "Shizuoka",  "Shizuoka",  Region.Chubu),
        new(23, "愛知県",   "Aichi",     "Aichi",     Region.Chubu),
        new(24, "三重県",   "Mie",       "Mie",       Region.Kansai),
        new(25, "滋賀県",   "Shiga",     "Shiga",     Region.Kansai),
        new(26, "京都府",   "Kyoto",     "Kyōto",     Region.Kansai),
        new(27, "大阪府",   "Osaka",     "Ōsaka",     Region.Kansai),
        new(28, "兵庫県",   "Hyogo",     "Hyōgo",     Region.Kansai),
        new(29, "奈良県",   "Nara",      "Nara",      Region.Kansai),
        new(30, "和歌山県", "Wakayama",  "Wakayama",  Region.Kansai),
        new(31, "鳥取県",   "Tottori",   "Tottori",   Region.Chugoku),
        new(32, "島根県",   "Shimane",   "Shimane",   Region.Chugoku),
        new(33, "岡山県",   "Okayama",   "Okayama",   Region.Chugoku),
        new(34, "広島県",   "Hiroshima", "Hiroshima", Region.Chugoku),
        new(35, "山口県",   "Yamaguchi", "Yamaguchi", Region.Chugoku),
        new(36, "徳島県",   "Tokushima", "Tokushima", Region.Shikoku),
        new(37, "香川県",   "Kagawa",    "Kagawa",    Region.Shikoku),
        new(38, "愛媛県",   "Ehime",     "Ehime",     Region.Shikoku),
        new(39, "高知県",   "Kochi",     "Kōchi",     Region.Shikoku),
        new(40, "福岡県",   "Fukuoka",   "Fukuoka",   Region.Kyushu),
        new(41, "佐賀県",   "Saga",      "Saga",      Region.Kyushu),
        new(42, "長崎県",   "Nagasaki",  "Nagasaki",  Region.Kyushu),
        new(43, "熊本県",   "Kumamoto",  "Kumamoto",  Region.Kyushu),
        new(44, "大分県",   "Oita",      "Ōita",      Region.Kyushu),
        new(45, "宮崎県",   "Miyazaki",  "Miyazaki",  Region.Kyushu),
        new(46, "鹿児島県", "Kagoshima", "Kagoshima", Region.Kyushu),
        new(47, "沖縄県",   "Okinawa",   "Okinawa",   Region.Kyushu)
    ];

    /// <summary>
    /// By JIS code. Built once — the pipeline looks up every code repeatedly.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, JisPrefecture> ByCode =
        All.ToDictionary(p => p.JisCode);

    /// <summary>
    /// By Japanese name. This is the index that joins <c>data/prefectures.geojson</c>
    /// to a JIS code, because the GeoJSON's only property is <c>N03_001</c>, the
    /// Japanese name. See DATA-SOURCES.md, "The name join".
    /// </summary>
    public static readonly IReadOnlyDictionary<string, JisPrefecture> ByNameJa =
        All.ToDictionary(p => p.NameJa, StringComparer.Ordinal);
}

/// <param name="JisCode">JIS X 0401 code, 1 (Hokkaidō) to 47 (Okinawa).</param>
/// <param name="NameJa">Japanese name, e.g. 東京都. Must match the GeoJSON exactly.</param>
/// <param name="NameEn">
/// Plain ASCII English name, without "Prefecture" and without macrons. This is the
/// form a URL slug or a case-insensitive search box can use.
/// </param>
/// <param name="NameRomaji">
/// Hepburn romanisation with macrons, e.g. Tōkyō. Separate from
/// <paramref name="NameEn"/> because the UI wants to display "Tōkyō" while search
/// has to match someone typing "tokyo".
/// </param>
/// <param name="Region">Conventional eight-region grouping. See the class remarks.</param>
public sealed record JisPrefecture(
    int JisCode,
    string NameJa,
    string NameEn,
    string NameRomaji,
    Region Region);
