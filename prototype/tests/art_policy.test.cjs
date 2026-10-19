// Deterministic policy tests; actual metadata, no live network.
const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const policy = require('../art-policy.js');
const catalog = JSON.parse(fs.readFileSync('prototype/data/catalog.json'));
const ocarina = 'https://cdn2.steamgriddb.com/hero/b63d7ec7840735552cb77df807d944ef.png';
test('every expected snapshot slot has an approved source and original fallback', () => {
  let count = 0;
  for (const app of catalog.apps) for (const kind of policy.KINDS) {
    assert.equal(policy.source(app, kind), app.referenceArtSources.libraryArt[kind]);
    assert.match(policy.local(app, kind), /^assets\/art\/[a-z0-9-]+\.webp$/);
    count++;
  }
  assert.equal(count, 36);
  assert.equal(policy.source(catalog.apps.find(a => a.projectName === 'Ship of Harkinian'), 'hero'), ocarina);
});
test('unsafe and ambiguous artwork sources fail closed', () => {
  for (const url of [null, {}, '', 'http://cdn2.steamgriddb.com/hero/'+'a'.repeat(32)+'.png',
    'https://user:pass@cdn2.steamgriddb.com/hero/'+'a'.repeat(32)+'.png',
    'https://cdn2.steamgriddb.com:444/hero/'+'a'.repeat(32)+'.png',
    'https://127.0.0.1/hero/'+'a'.repeat(32)+'.png', 'https://[::1]/art.png',
    'https://cdn2.steamgriddb.com.evil.invalid/hero/'+'a'.repeat(32)+'.png',
    'https://cdn2.steamgriddb.com/hero/../hero/'+'a'.repeat(32)+'.png',
    ocarina+'?tracking=1', ocarina+'#fragment', ocarina+'\n',
    'data:image/png;base64,AAA', 'file:///art.png', 'javascript:alert(1)',
    'https://cdn2.steamgriddb.com/logo/'+'a'.repeat(32)+'.png',
    'https://cdn2.steamgriddb.com/hero/\\'+ 'a'.repeat(32)+'.png']) assert.equal(policy.approved(url), null, String(url));
  assert.equal(policy.approved(ocarina), ocarina);
});
test('selection is slot-specific; untrusted metadata cannot inject another source', () => {
  const app = {referenceArtSources:{libraryArt:{hero:ocarina,header:'https://evil.invalid/a.png'}},localArt:{hero:'assets/art/one.webp',header:'../secret.webp'}};
  assert.equal(policy.source(app, 'hero'), ocarina);
  assert.equal(policy.source(app, 'header'), null);
  assert.equal(policy.local(app, 'header'), null);
  assert.equal(policy.source(app, 'logo'), null);
});
test('completeness diagnostic validates all 36 expected slots and explains repair', () => {
  const {inspect} = require('../../tools/check_artwork.cjs');
  const report=inspect(catalog);
  assert.equal(report.expected,36);assert.equal(report.complete,36);assert.equal(report.failures.length,0);
  const broken=structuredClone(catalog);
  broken.apps[0].referenceArtSources.libraryArt.hero='https://attacker.invalid/x.png';
  broken.apps[1].localArt.header='assets/art/not-present.webp';
  const failures=inspect(broken).failures;
  assert.equal(failures.length,2);
  assert.equal(failures[0].kind,'hero');assert.match(failures[0].action,/source/i);
  assert.equal(failures[1].kind,'header');assert.match(failures[1].action,/fallback/i);
});
