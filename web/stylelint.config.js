/**
 * Stylelint reports probable mistakes in the CSS. stylelint-config-recommended
 * enables only rules that find errors, for example an unknown property or a
 * duplicate selector. It enables no layout rules: Prettier formats the CSS.
 *
 * The two unit rules below allow only rem for sizes, and px only on border
 * properties. A px value that must stay fixed needs a stylelint-disable comment
 * that says why.
 *
 * @type {import('stylelint').Config}
 */
export default {
  extends: ['stylelint-config-recommended'],
  rules: {
    'declaration-property-unit-allowed-list': {
      '/^font-size$|^line-height$/': ['rem'],
      'letter-spacing': ['em'],
      '/^(padding|margin|gap|row-gap|column-gap|inset|top|right|bottom|left)/': ['rem'],
      '/^(min-|max-)?(width|height)$/': ['rem', '%', 'dvh', 'svh', 'vh', 'vw'],
      '/^grid-template-/': ['rem', 'fr', '%'],
      '/^outline/': ['rem', 'em'],
      '/^border(-[a-z]+)*-radius$/': ['rem', '%'],
      '/^box-shadow$|^-webkit-text-stroke/': ['rem']
    },
    'media-feature-name-unit-allowed-list': {
      '/width$|height$/': ['rem']
    }
  }
};
