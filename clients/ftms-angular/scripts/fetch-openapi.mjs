// Pulls the OpenAPI document from the running API and snapshots it into docs/api/.
//
// design: doc 05 section 9 and doc 10 section 2 - OpenAPI is generated from the code itself,
// so it is never stale by construction, and a snapshot is committed so contract history is
// diffable. The generator then runs against the snapshot rather than the live API, which means
// `npm ci && npm run build` works in CI without a backend running.
//
// Regenerate with: npm run generate:api   (needs the API running on http://localhost:5150)
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const target = resolve(here, '../../../docs/api/openapi-v1.json');
const source = process.env.FTMS_OPENAPI_URL ?? 'http://localhost:5150/openapi/v1.json';

try {
  const response = await fetch(source);

  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText}`);
  }

  const document = await response.json();
  await mkdir(dirname(target), { recursive: true });

  // Pretty printed so a contract change shows up as a readable diff rather than one long line.
  await writeFile(target, `${JSON.stringify(document, null, 2)}\n`, 'utf8');

  console.log(`Snapshotted ${source} -> ${target}`);
} catch (error) {
  console.error(`Could not fetch the OpenAPI document from ${source}.`);
  console.error('Start the API first:  dotnet run --project src/FTMS.Api');
  console.error(String(error));
  process.exit(1);
}
