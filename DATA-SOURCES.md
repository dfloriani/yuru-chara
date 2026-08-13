# Data sources

Every external source this project takes data from, with its URL, its licence,
the attribution it requires, the date it was retrieved, and exactly what was
taken from it.

Nothing in `data/` may come from a source that is not listed here.

---

## 1. Prefecture boundaries

**File produced:** `data/prefectures.geojson`
**SHA-256:** `234914ee3a13beea6ef25f148e5c3a1ba6d0d1f0d0d8eb2af6a1732792ecf3ed`
**Retrieved:** 2026-08-13

### What was taken

A single GeoJSON `FeatureCollection` of 47 features, one per prefecture. It is
committed byte-for-byte as downloaded, so the SHA-256 above can be checked
against the source at any time.

Each feature carries exactly one property:

```json
{ "N03_001": "北海道" }
```

`N03_001` is the prefecture name in Japanese. **There is no JIS X 0401 code in
the file.** The seeder joins on `N03_001` — see "The name join" below.

### Immediate source

| | |
|---|---|
| Publisher | SmartNews Media Research Institute (スマートニュース メディア研究所) |
| Repository | <https://github.com/smartnews-smri/japan-topography> |
| File | `data/municipality/geojson/s0010/prefectures.json` |
| Direct URL | <https://raw.githubusercontent.com/smartnews-smri/japan-topography/main/data/municipality/geojson/s0010/prefectures.json> |
| Terms | Repository `README.md`, section クレジット |

The repository's own terms state that crediting SmartNews as the processor is
not required, and that the data is free to use commercially and
non-commercially:

> データを使用する際に、加工者としてスマートニュースおよびスマートニュース メディア研究所の名前をクレジットする必要はありません。
> 商用・非商用にかかわらず、無償でお使いいただけます。
> ただし市区町村データは、国土交通省の指示するクレジット記載が必要です。

The last line is the one that binds us: the underlying municipal data carries
MLIT's attribution requirement, and prefecture boundaries were derived from it.

### Upstream source and licence

| | |
|---|---|
| Publisher | 国土交通省 (Ministry of Land, Infrastructure, Transport and Tourism, MLIT) |
| Dataset | 国土数値情報 行政区域データ (National Land Numerical Information, Administrative Divisions), N03 |
| Dataset page | <https://nlftp.mlit.go.jp/ksj/gml/datalist/KsjTmplt-N03-2024.html> |
| Site terms | <https://nlftp.mlit.go.jp/ksj/other/agreement.html> |
| Licence stated on the dataset page | このデータの使用許諾条件: **オープンデータ（CC_BY_4.0）** — for FY2018 (平成30年度) onwards, 適用する利用規約に基づく（オープンデータ） |
| Site-wide licence | 公共データ利用規約（第1.0版）(PDL 1.0), <https://www.digital.go.jp/resources/open_data/public_data_license_v1.0> |
| Vintage of the data SmartNews processed | N03 as of 2021-01-01, retrieved by SmartNews 2021-09-28 |

PDL 1.0 is the Japanese government's standard open-data licence and is
compatible with CC BY 4.0. Reuse, including commercial reuse and modification,
is permitted with attribution.

**Licence verified before committing the file.** This is the check CLAUDE.md
asks for, and the answer is that both hops are open: the immediate source
imposes no condition of its own, and the upstream source is CC BY 4.0 / PDL 1.0
with attribution.

### Attribution required

MLIT's terms require a source statement, and require *separately* that any
editing or processing be declared:

> コンテンツを利用する際は出典を記載してください。（…）また、コンテンツを編集・加工等して利用する場合は、以下の出典とは別に、利用したコンテンツの名称及び編集・加工等を行ったことを記載してください。なお、編集・加工した情報を、あたかも国が作成したかのような態様で公表・利用してはいけません。

The geometry in this repository **has** been processed — simplified to 1% by
SmartNews, and simplified further at read time by this application — so the
second requirement applies and the first is not sufficient on its own.

**The string the UI footer must carry**, in full:

> 「国土数値情報（行政区域データ）」（国土交通省）
> <https://nlftp.mlit.go.jp/ksj/gml/datalist/KsjTmplt-N03-2024.html>
> をもとにスマートニュース メディア研究所および本プロジェクトが作成（境界線を簡素化）

In English, for the same footer:

> Contains information from the National Land Numerical Information
> (Administrative Divisions) published by the Ministry of Land, Infrastructure,
> Transport and Tourism of Japan, processed by SmartNews Media Research
> Institute and by this project (boundaries simplified). Not produced by, or
> endorsed by, the Government of Japan.

The final sentence is not decoration. It is the 「あたかも国が作成したかのような態様で公表・利用してはいけません」
requirement: we must not present processed data as if the government made it.

### Caveat recorded verbatim

The dataset page carries this note, and it is recorded here rather than left
out:

> ※本データを二次利用する場合には、国土地理院に申請等必要な場合があります。

Secondary use *may* in some cases require an application to the Geospatial
Information Authority of Japan. This attaches to reproduction of basic survey
results under the Survey Act (測量法). Drawing simplified administrative
boundaries on a map is the ordinary published use of the open-data N03 release
and is not reproduction of a basic survey result, so no application was made.
If this project ever traces, overlays, or redistributes GSI base map imagery,
this note has to be revisited.

### Simplification tolerance — the number that caps everything

The committed file is the **1% (`s0010`)** build, not the 0.1% (`s0001`) one:

| Build | Vertices | File size |
|---|---:|---:|
| `s0010` — 1%, committed | 61,033 | 2.4 MB |
| `s0001` — 0.1%, not committed | 8,007 | 0.32 MB |

Per DECISIONS.md 12, this tolerance is the **maximum detail any
`?detail=high` request can ever return.** `ST_Simplify` at read time can
remove detail; nothing can put it back. Raising the ceiling means re-sourcing
the boundary file, not changing an API parameter.

1% was chosen because 0.1% visibly destroys the island geography that makes
this map interesting — Nagasaki drops from 4,071 vertices to a few hundred —
and 2.4 MB in the repository is cheap. The 0.1% build is roughly what a
`?detail=low` response looks like after `ST_Simplify`, which is the correct
place for that reduction because it varies by viewport.

### Coordinate reference system

The dataset page gives the source CRS as **JGD2011 (B, L)** — geographic
latitude and longitude on the Japanese Geodetic Datum 2011, EPSG:6668.

This project stores and serves everything as **SRID 4326** (WGS84) and does
not reproject. JGD2011 and WGS84 differ by a few centimetres in Japan, which
is far below the error already introduced by 1% simplification and far below
one screen pixel at any zoom level this map uses. Labelling the geometry 4326
is therefore accurate for its purpose and wrong only in a sense that cannot be
observed here. Recorded because it is a real approximation, not because it
needs fixing.

### The name join

The file has no JIS code, so `src/YuruChara.Ingestion/Seeding/JisPrefectures.cs`
joins each feature to a prefecture by its Japanese name.

This was checked, not assumed: all 47 `N03_001` values match the Japanese
labels of the 47 items returned by the Wikidata query in section 2 exactly,
character for character, and the features happen to be in JIS code order
(feature 1 = 北海道 = code 1, feature 47 = 沖縄県 = code 47). The seeder still
joins by name rather than by array position, and fails if any name is
unmatched or if the count is not 47, because file order is not a documented
guarantee of the source.

### Geometry note

12 of the 47 features are `Polygon` and 35 are `MultiPolygon`. The domain model
requires `MultiPolygon` throughout, so the seeder promotes single polygons.
See the comment in `PrefectureBoundaryReader`.

### Alternative considered

Downloading N03 directly from MLIT and simplifying it here. Rejected for now:
it means handling a shapefile or GML input, the raw download is tens of
megabytes for detail this map will never draw, and it would put a
simplification tolerance of our own choosing into the pipeline without making
the map any better. The cost of not doing it is that the vintage is fixed at
SmartNews's 2021 retrieval and the 1% ceiling is theirs rather than ours.
Prefecture boundaries, unlike municipal ones, essentially do not change, so the
vintage matters much less here than it would for the municipal file.

---

## 2. Mascot data — Wikidata

**Query:** `src/YuruChara.Ingestion/Wikidata/prefecture-mascots.rq` (committed,
and the file the ingestion CLI actually executes)
**Endpoint:** <https://query.wikidata.org/sparql>
**Service:** Wikidata Query Service, <https://query.wikidata.org/>
**Retrieved:** 2026-08-13
**Licence:** CC0 1.0 Universal — Wikidata item data is dedicated to the public
domain, <https://www.wikidata.org/wiki/Wikidata:Licensing>
**Attribution required:** none. Credited anyway, in every affected
`SourceCitations` entry, because provenance is the point.

### What was taken

Per mascot: Japanese and English labels, inception date (P571), official
website (P856), and `P31` classes. Per prefecture: the JIS code, via the
numeric suffix of ISO 3166-2 (P300).

### How prefectures are identified

`?pref wdt:P31 wd:Q50337` on its own is wrong: it also returns the prefectures
abolished in the 1870s (Iwai, Chikuma, Okitama and others). Requiring `P300`
and filtering to `JP-` returns exactly 47 rows, and the numeric suffix of
`JP-NN` **is** the JIS X 0401 code, which is the key this project uses. So one
constraint both restricts the query to current prefectures and produces the
join key.

### How mascots are reached

Two directions, unioned:

1. `?pref wdt:P822 ?mascot` — the prefecture asserts its mascot.
2. `?mascot wdt:P6291 ?pref` — the character asserts what it advertises.

Direction 2 was added after direction 1 alone missed Akita, whose mascot
んだッチ (Q110529296) exists on Wikidata with no `P822` statement on the
prefecture side. It contributed exactly one prefecture.

A class filter is applied, because these two properties also pick up things
that are not mascots. `彩の国` (Q11487969, an advertising slogan),
`リメンバーしまね` (Q11348471, an online community) and `ゆる玉応援団`
(Q11281003, a fictional *organisation* — an umbrella group of Saitama's local
mascots, not a mascot) are all excluded. Group characters such as
わんこきょうだい, Juratic and みやざき犬 are **not** excluded: a trio or a set of
siblings can perfectly well be a prefecture's mascot.

### Coverage achieved

33 of 47 prefectures. 35 mascot records. The 14 uncovered prefectures are
listed in `data/prefecture-mascots.json` with a null mascot, and in section 4
below.

### What this source is trusted for

`SourceReliability.Aggregated`. Everything taken from it lands at
`VerificationLevel.Automated`. It was wrong about two debut years and three
official URLs out of the eleven records that were checked by hand — see
section 3.

### Not used

The eight-region grouping (Hokkaidō / Tōhoku / Kantō / Chūbu / Kansai /
Chūgoku / Shikoku / Kyūshū) was **not** taken from Wikidata. Prefecture items
do not carry a queryable `P361` or `P131` statement pointing at region items,
so the query returns nothing for it. The grouping is the conventional one
listed in CLAUDE.md and is written out in
`src/YuruChara.Ingestion/Seeding/JisPrefectures.cs`. It is a standard
classification, not research, and it is not a fact about any mascot.

---

## 3. Mascot data — owning bodies' own websites

**Retrieved:** 2026-08-13
**Licence:** none granted, and none needed. No text, image, or asset from these
sites is copied into this repository. What was taken is factual data — a debut
year, a motif, the name of the department that owns the character, and whether
using the artwork requires an application — recorded in our own words.
**Attribution:** every fact taken this way carries a `SourceCitation` at
`SourceReliability.Official` naming the exact page and this date.

These are the pages the eleven `ManuallyVerified` mascots were checked against.
Facts confirmed or corrected from them are marked `Official` in the seed file;
fields these pages simply do not state stay at `Aggregated` with Wikidata as
the citation, rather than being upgraded by association.

| JIS | Mascot | Page read |
|---:|---|---|
| 07 | キビタン | <https://www.pref.fukushima.lg.jp/site/kibitanroom/>, <https://www.pref.fukushima.lg.jp/site/kibitanroom/riyoshinsei.html> |
| 09 | とちまるくん | <https://www.tochimarukun.jp/profile/> |
| 10 | ぐんまちゃん | <https://gunmachan-official.jp/profile/>, <https://gunmachan-official.jp/30th/> |
| 11 | コバトン | <https://www.pref.saitama.lg.jp/a0301/kobaton/tanjo.html> |
| 12 | チーバくん | <https://www.pref.chiba.lg.jp/kouhou/miryoku/chi-ba-kun/profile.html> |
| 20 | アルクマ | <https://arukuma.jp/about> |
| 27 | もずやん | <https://www.pref.osaka.lg.jp/o070050/koho/character2/mozuyan_profile.html> |
| 32 | しまねっこ | <https://www.kankou-shimane.com/shimanekko/>, <https://www.kankou-shimane.com/shimanekko/design/> |
| 38 | みきゃん | <https://www.pref.ehime.jp/site/mican/>, <https://www.pref.ehime.jp/site/mican/16839.html> |
| 43 | くまモン | <https://kumamon-land.jp/profile/>, <https://kumamon-land.jp/riyokyodaku/> |
| 44 | めじろん | <https://www.pref.oita.jp/site/mejiron/profile.html> |

### Pages that could not be read

- **せんとくん (29), Nara Prefecture** — <https://www.pref.nara.jp/item/125614.htm>
  returns HTTP 403 to requests from this environment. Sento-kun was therefore
  **not** promoted and stays at `Automated`. Recorded because "we could not
  read the page" and "the page does not say" are different failures, and only
  the second one is a fact about the mascot.
- **ひゃくまんさん (17), Ishikawa Prefecture** — <https://hyakumansan.jp/> and
  its `design` page render their content as images with no readable text, so
  neither the debut year nor the usage terms could be confirmed. Not promoted.

---

## 4. Prefectures with no mascot data

14 prefectures have an entry in `data/prefecture-mascots.json` with an empty
`"mascots": []`. Wikidata yielded nothing for them under the query in section 2,
and **nothing was filled in from memory.** Per CLAUDE.md: a visibly incomplete
map is fine, a confidently wrong one is not.

An empty list rather than a missing entry, and rather than a single nullable
`mascot`: all 47 prefectures are always present, so a gap is a recorded state
that the seeder counts and reports, and the list handles Shiga and Ehime, which
genuinely have two mascots each.

| JIS | Prefecture | | JIS | Prefecture |
|---:|---|---|---:|---|
| 01 | 北海道 Hokkaidō | | 30 | 和歌山県 Wakayama |
| 02 | 青森県 Aomori | | 34 | 広島県 Hiroshima |
| 08 | 茨城県 Ibaraki | | 37 | 香川県 Kagawa |
| 13 | 東京都 Tokyo | | 40 | 福岡県 Fukuoka |
| 19 | 山梨県 Yamanashi | | 41 | 佐賀県 Saga |
| 23 | 愛知県 Aichi | | 42 | 長崎県 Nagasaki |
| 24 | 三重県 Mie | | 47 | 沖縄県 Okinawa |

A null here means "Wikidata has no linked mascot for this prefecture". It does
not mean the prefecture has none. Several of these are known to have
prefectural characters; the gap is in the automated source, and closing it
needs a per-prefecture check against the owning body's own site, which is the
same work as section 3 and is not guessing.

### Not used to fill these gaps

The **Fandom Yuru-chara wiki** is named in CLAUDE.md as usable to fill gaps at
lower confidence. It was not used, and `SourceReliability.Community` is
currently unused in the seed file. Reason: for these 14 prefectures the missing
fact is *which character is the official prefectural mascot*, and that is
exactly the claim a community wiki is least reliable about — it lists municipal,
corporate and unofficial characters alongside official ones without
consistently distinguishing them. A wrong answer there would be recorded as a
mascot rather than as a gap, which is the failure mode CLAUDE.md rules out.
The enum member stays because that judgement is per-fact, not permanent: it is
a reasonable source for a motif or a debut year on a mascot we have already
identified.

---

## 5. Not used, and why

- **Google Maps Platform boundaries** — rejected in CLAUDE.md. Its terms forbid
  storing or caching the geometry, which is incompatible with a PostGIS
  database being the point of the project.
- **GADM** — its licence forbids commercial use and redistribution.
- **地球地図日本 (Global Map Japan)**, the source behind
  `dataofjapan/land`, the most commonly linked Japan prefecture GeoJSON —
  requires attribution plus, for commercial use, a report to the copyright
  holder. That is a more restrictive condition than the N03 route and there is
  no reason to accept it.
- **Natural Earth admin-1** — public domain and would have been acceptable, but
  at 1:10m its Japanese island detail is poorer than the N03 1% build, and it
  keys on ISO codes rather than JIS.
- **Mascot images, of any kind, from any source** — see CLAUDE.md, "Image
  licensing". This is a hard rule, not a gap. `ImageLicenseStatus` records what
  each owning body's terms actually say instead.
