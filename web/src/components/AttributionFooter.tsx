import './attribution-footer.css';

const N03_DATASET_URL = 'https://nlftp.mlit.go.jp/ksj/gml/datalist/KsjTmplt-N03-2024.html';

const AUTHOR_URL = 'https://github.com/dfloriani';

/**
 * The boundary attribution, in the form DATA-SOURCES.md says the UI must carry,
 * and the author credit.
 *
 * The attribution is a licence condition, not a credit. MLIT's terms for the
 * National Land Numerical Information require a source statement *and* —
 * separately — a declaration that the content was edited, because the geometry
 * here has been processed twice: simplified to 1% by SmartNews before it was
 * committed, and simplified again by `ST_Simplify` on every request.
 *
 * The Japanese string is the one the terms specify. It is reproduced verbatim and
 * it is never behind a toggle. The English paragraph is a rendering of it for
 * readers of this app rather than a substitute — including the sentence about
 * endorsement, which is MLIT's third requirement: processed data must not be
 * presented as if the government produced it. That paragraph starts closed at
 * every width, because an open paragraph takes height that the map needs.
 */
export function AttributionFooter() {
  return (
    <footer className="attribution">
      <p className="attribution__source" lang="ja">
        「国土数値情報（行政区域データ）」（
        <a href={N03_DATASET_URL} target="_blank" rel="noreferrer noopener">
          国土交通省
        </a>
        ）をもとにスマートニュース メディア研究所および本プロジェクトが作成（境界線を簡素化）
      </p>
      <div className="attribution__row">
        <details className="attribution__details">
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
        <p className="attribution__credit">
          <a href={AUTHOR_URL} target="_blank" rel="noreferrer noopener">
            <GitHubMark />
            dfloriani aka Kinmamon
            {/* The icon is hidden from screen readers, so the link name says
                where the link goes in words. */}
            <span className="visually-hidden"> on GitHub</span>
          </a>
        </p>
      </div>
    </footer>
  );
}

function GitHubMark() {
  return (
    <svg
      className="attribution__icon"
      viewBox="0 0 16 16"
      aria-hidden="true"
      focusable="false"
      fill="currentColor"
    >
      <path d="M8 0c4.42 0 8 3.58 8 8a8.013 8.013 0 0 1-5.45 7.59c-.4.08-.55-.17-.55-.38 0-.27.01-1.13.01-2.2 0-.75-.25-1.23-.54-1.48 1.78-.2 3.65-.88 3.65-3.95 0-.88-.31-1.59-.82-2.15.08-.2.36-1.02-.08-2.12 0 0-.67-.22-2.2.82-.64-.18-1.32-.27-2-.27-.68 0-1.36.09-2 .27-1.53-1.03-2.2-.82-2.2-.82-.44 1.1-.16 1.92-.08 2.12-.51.56-.82 1.28-.82 2.15 0 3.06 1.86 3.75 3.64 3.95-.23.2-.44.55-.51 1.07-.46.21-1.61.55-2.33-.66-.15-.24-.6-.83-1.23-.82-.67.01-.27.38.01.53.34.19.73.9.82 1.13.16.45.68 1.31 2.69.94 0 .67.01 1.3.01 1.49 0 .21-.15.45-.55.38A7.995 7.995 0 0 1 0 8c0-4.42 3.58-8 8-8Z" />
    </svg>
  );
}
