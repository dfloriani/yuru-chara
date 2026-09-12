import { useEffect } from 'react';
import type { ImageLicenseStatus, Mascot } from '../api/types';
import { COVERAGE } from '../data/coverage';
import type { PrefectureEntry } from '../data/prefectures';
import './detail-panel.css';

/**
 * The selected prefecture and its mascots.
 *
 * One component, two shapes. CLAUDE.md asks for a bottom sheet on a narrow
 * viewport and a side panel on a wide one, decided up front rather than
 * retrofitted — so the difference is a class name and a stylesheet, and the
 * contents are written once.
 *
 * Non-modal in both shapes: the map and the list stay live behind it, and
 * selecting a different prefecture replaces what is in here. That is why there is
 * no focus trap, and why Escape closes it rather than a backdrop that would have
 * to block the very thing it sits over.
 */
export function DetailPanel({
  entry,
  variant,
  onClose
}: {
  readonly entry: PrefectureEntry;
  readonly variant: 'sheet' | 'panel';
  readonly onClose: () => void;
}) {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  const coverage = COVERAGE[entry.coverage];

  return (
    <aside
      className={`detail detail--${variant}`}
      aria-label={`${entry.nameEn} Prefecture details`}
      // The panel's contents are replaced when another prefecture is selected
      // rather than being announced as a new region, so a screen reader is told
      // that this area updates.
      aria-live="polite"
    >
      <header className="detail__header">
        <div className="detail__titles">
          <h2 className="detail__title">
            {entry.nameEn}
            <span className="detail__title-ja" lang="ja">
              {entry.nameJa}
            </span>
          </h2>
          <p className="detail__subtitle">
            {entry.regionLabel} · JIS {entry.jisCode}
          </p>
        </div>
        <button type="button" className="detail__close" onClick={onClose}>
          <span aria-hidden="true">✕</span>
          <span className="visually-hidden">Close details</span>
        </button>
      </header>

      <div className="detail__body">
        {entry.mascots.length === 0 ? (
          // The plain statement CLAUDE.md asks for. A prefecture with no mascot
          // is a recorded gap in the automated source, and saying that is more
          // honest — and more useful — than an empty panel or an invented record.
          <div className="detail__empty">
            <p className="detail__empty-headline">No verified mascot data yet.</p>
            <p>
              The automated pass over Wikidata found no prefecture-level mascot for {entry.nameEn},
              and nothing was filled in by hand. 14 of the 47 prefectures are in this state. Nothing
              is guessed here: a blank is recorded as a blank.
            </p>
          </div>
        ) : (
          <>
            <p className="detail__coverage" style={{ color: coverage.fill }}>
              {coverage.label} — {coverage.description}
            </p>
            {entry.mascots.map((mascot) => (
              <MascotCard key={mascot.id} mascot={mascot} />
            ))}
          </>
        )}
      </div>
    </aside>
  );
}

function MascotCard({ mascot }: { readonly mascot: Mascot }) {
  return (
    <article className="mascot">
      <h3 className="mascot__name">
        {/* Kana first and at size, romaji beneath. The Japanese name is the
            mascot's actual name; the romaji is a reading of it. */}
        <span className="mascot__name-ja" lang="ja">
          {mascot.nameJa}
        </span>
        {mascot.nameRomaji !== null && (
          <span className="mascot__name-romaji">{mascot.nameRomaji}</span>
        )}
      </h3>

      <p className="mascot__tags">
        <span
          className={`mascot__tag mascot__tag--${mascot.isOfficial ? 'official' : 'unofficial'}`}
        >
          {mascot.isOfficial ? 'Official mascot' : 'Unofficial mascot'}
        </span>
        {/* Coverage and confidence are separate fields, and this is the
            confidence one. See DECISIONS.md 6. */}
        <span
          className={`mascot__tag mascot__tag--${
            mascot.verificationLevel === 'ManuallyVerified' ? 'verified' : 'automated'
          }`}
        >
          {mascot.verificationLevel === 'ManuallyVerified'
            ? 'Manually verified'
            : 'Automated, not hand-checked'}
        </span>
      </p>

      <dl className="mascot__facts">
        <Fact label="Motif" value={mascot.motif} />
        <Fact label="Debut" value={mascot.debutYear === null ? null : String(mascot.debutYear)} />
        <Fact label="Owner" value={mascot.owningBody} />
      </dl>

      {mascot.officialUrl !== null && (
        <p className="mascot__link">
          <a href={mascot.officialUrl} target="_blank" rel="noreferrer noopener">
            Official site
            <span className="mascot__link-host"> {hostOf(mascot.officialUrl)}</span>
          </a>
        </p>
      )}

      {/* There is no picture here and there will not be one. Saying why, from
          researched data rather than silence, is the whole reason
          ImageLicenseStatus exists. See CLAUDE.md, "Image licensing". */}
      <p className="mascot__licence">
        <span className="mascot__licence-label">Image</span>{' '}
        {IMAGE_LICENCE_TEXT[mascot.imageLicenseStatus]}
        {mascot.licenseNotes !== null && <> {mascot.licenseNotes}</>}
      </p>

      {mascot.sourceCitations.length > 0 && (
        // Collapsed by default. Being able to answer "where did that debut year
        // come from?" is part of the point of this project, but it is not what
        // someone opening a panel on a phone came to read.
        <details className="mascot__sources">
          <summary>Sources ({mascot.sourceCitations.length})</summary>
          <ul>
            {mascot.sourceCitations.map((citation) => (
              <li key={`${citation.field}-${citation.url}`}>
                <span className="mascot__source-field">{citation.field}</span>{' '}
                <a href={citation.url} target="_blank" rel="noreferrer noopener">
                  {citation.sourceName}
                </a>{' '}
                <span className="mascot__source-meta">
                  {citation.reliability.toLowerCase()}, read {citation.retrievedOn}
                </span>
              </li>
            ))}
          </ul>
        </details>
      )}
    </article>
  );
}

/**
 * One labelled fact, or an explicit "not recorded".
 *
 * A missing field is rendered rather than skipped, because a panel that quietly
 * omits the debut year looks the same as one for a mascot with no debut year, and
 * those are different facts.
 */
function Fact({ label, value }: { readonly label: string; readonly value: string | null }) {
  return (
    <>
      <dt>{label}</dt>
      <dd className={value === null ? 'mascot__fact--missing' : undefined}>
        {value ?? 'Not recorded'}
      </dd>
    </>
  );
}

/** What each licence status means, in a sentence a visitor can act on. */
const IMAGE_LICENCE_TEXT: Record<ImageLicenseStatus, string> = {
  Unknown: 'Licence not researched yet. No image is shown.',
  ApplicationRequired: 'The owning body requires a usage application. No image is shown.',
  OfficialMaterialsPublished:
    'The owning body publishes assets with terms. No image is shown here.',
  CommonsFreeLicense:
    'A freely licensed photograph exists on Wikimedia Commons. No image is shown here.',
  NoReuseGranted: 'The terms forbid third-party reuse. No image is shown.'
};

/** The bare host, so a link says where it goes without printing a full URL. */
function hostOf(url: string): string {
  try {
    return new URL(url).host;
  } catch {
    // The value comes from a .NET Uri, so this should not happen — but a bad seed
    // record must not blank the panel it appears in.
    return url;
  }
}
