/**
 * Stylelint reports probable mistakes in the CSS. stylelint-config-recommended
 * enables only rules that find errors, for example an unknown property or a
 * duplicate selector. It enables no layout rules: Prettier formats the CSS.
 *
 * @type {import('stylelint').Config}
 */
export default {
  extends: ['stylelint-config-recommended']
};
