/* Shared by the browser, local artwork worker and policy tests. */
'use strict';
((root) => {
  const KINDS = Object.freeze(['hero', 'header', 'capsule']);
  // Match the ORIGINAL spelling, before URL normalization (no traversal, escapes,
  // query, fragment, alternate port, credentials or ambiguous authority).
  const pattern = /^https:\/\/cdn2\.steamgriddb\.com\/(hero|grid)\/[a-f0-9]{32}\.(png|jpg|webp)$/;
  function approved(value) { return typeof value === 'string' && value.length <= 128 && pattern.test(value) ? value : null; }
  function source(app, kind) { return KINDS.includes(kind) ? approved(app?.referenceArtSources?.libraryArt?.[kind]) : null; }
  function local(app, kind) { const s = app?.localArt?.[kind]; return KINDS.includes(kind) && typeof s === 'string' && /^assets\/art\/[a-z0-9-]+\.webp$/.test(s) ? s : null; }
  const api = Object.freeze({KINDS, approved, source, local});
  if (typeof module !== 'undefined') module.exports = api;
  else root.RelicadeArtPolicy = api;
})(globalThis);
