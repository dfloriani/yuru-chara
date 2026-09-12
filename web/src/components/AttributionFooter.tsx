import './attribution-footer.css';

const N03_DATASET_URL = 'https://nlftp.mlit.go.jp/ksj/gml/datalist/KsjTmplt-N03-2024.html';

/**
 * The boundary attribution, in the form DATA-SOURCES.md says the UI must carry.
 *
 * This is a licence condition, not a credit. MLIT's terms for the National Land
 * Numerical Information require a source statement *and* — separately — a
 * declaration that the content was edited, because the geometry here has been
 * processed twice: simplified to 1% by SmartNews before it was committed, and
 * simplified again by `ST_Simplify` on every request.
 *
 * The Japanese string is the one the terms specify. It is reproduced verbatim and
 * it is always on screen, at every viewport width. The English paragraph is a
 * rendering of it for readers of this app rather than a substitute — including
 * the sentence about endorsement, which is MLIT's third requirement: processed
 * data must not be presented as if the government produced it. On a phone that
 * paragraph would be a sixth of the screen, so it opens on demand there and is
 * open from the start wherever there is room.
 */
export function AttributionFooter({ expanded }: { readonly expanded: boolean }) {
  return (
    <footer className="attribution">
      <p className="attribution__source" lang="ja">
        「国土数値情報（行政区域データ）」（
        <a href={N03_DATASET_URL} target="_blank" rel="noreferrer noopener">
          国土交通省
        </a>
        ）をもとにスマートニュース メディア研究所および本プロジェクトが作成（境界線を簡素化）
      </p>
      <details className="attribution__details" open={expanded}>
        <summary>Sources and licensing, in English</summary>
        <p>
          Boundaries: National Land Numerical Information (Administrative Divisions), Ministry of
          Land, Infrastructure, Transport and Tourism of Japan, processed by SmartNews Media
          Research Institute and by this project (boundaries simplified). Not produced by, or
          endorsed by, the Government of Japan.
        </p>
        <p>
          Mascot facts: Wikidata, and the owning bodies’ own sites for the hand-checked records.
          Each mascot lists its own sources. No mascot images are used anywhere in this project —
          the designs are copyrighted by the prefectures that own them.
        </p>
      </details>
    </footer>
  );
}
