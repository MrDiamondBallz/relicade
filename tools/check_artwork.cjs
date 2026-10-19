/* Offline metadata/fallback completeness, NOT production image availability.
 * For live decoded pixels use Artwork & privacy > Check all artwork slots.
 */
'use strict';
const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const P = require('../prototype/art-policy.js');
const ROOT = path.resolve(__dirname, '..');
function inspect(snapshot) {
  const failures=[], slots=[];
  if(!Array.isArray(snapshot?.apps))return {expected:0,complete:0,failures:[{action:'Restore a valid catalog apps array'}],slots};
  for(const item of snapshot.apps)for(const kind of P.KINDS){
    const app=item && typeof item==='object'?item:{};
    const source=P.source(app,kind), local=P.local(app,kind), problems=[];
    if(!source)problems.push('Review and restore an approved source URL from catalog provenance');
    const provenance=Array.isArray(app.artProvenance)?app.artProvenance.find(p=>p?.kind===kind && p.localPath===local):null;
    try {
      if(!local)throw Error('Missing safe path');
      const root=fs.realpathSync(path.join(ROOT,'prototype/assets/art'));
      const file=fs.realpathSync(path.join(ROOT,'prototype',local));
      if(!file.startsWith(root+path.sep))throw Error('Escaped fallback path');
      const raw=fs.readFileSync(file);
      const hash=crypto.createHash('sha256').update(raw).digest('hex');
      if(hash!==provenance?.sha256)throw Error('Fallback hash mismatch');
    }catch(_){problems.push('Repair original fallback using project_art.py and review its provenance/hash');}
    const row={project:app.slug,kind,source,local,metadataComplete:!problems.length};
    slots.push(row);
    if(problems.length)failures.push({project:app.slug,kind,action:problems.join('; ')});
  }
  return {scope:'Offline metadata and original fallback validation only; no live image availability claim',expected:slots.length,complete:slots.length-failures.length,failures,slots};
}
module.exports={inspect};
if(require.main===module){
  const report=inspect(JSON.parse(fs.readFileSync(path.join(ROOT,'prototype/data/catalog.json'),'utf8')));
  console.log(JSON.stringify(report,null,2));
  process.exitCode=report.failures.length?1:0;
}
