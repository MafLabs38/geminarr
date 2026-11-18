// Packs the extension for the stack, with a key made for this run, and writes what a deployment would give the browser: the policy that installs it and gives it its address and its token.
// The assistant of the stack is a fake on a plain-HTTP host, so the package is built for that origin (a release is built for the real one).
import { mkdirSync, writeFileSync } from 'node:fs';

import { pack, writePackage } from '../../../../tools/extension/pack.mjs';

const { TOKEN, GEMINARR_URL, ASSISTANT_ORIGIN } = process.env;
const packed = pack({ source: '/extension-source', version: '0.0.1', origin: ASSISTANT_ORIGIN });
writePackage('/package', packed);

mkdirSync('/policy', { recursive: true });
writeFileSync(
  '/policy/geminarr.json',
  JSON.stringify({
    // The only extension is Geminarr's reader; nobody opens the developer tools or starts a debugging port in a browser that holds a session; the pages it may load are the assistant's.
    ExtensionSettings: {
      '*': { installation_mode: 'blocked' },
      [packed.id]: { installation_mode: 'force_installed', update_url: `${GEMINARR_URL}/api/v1/extension/update.xml` },
    },
    '3rdparty': { extensions: { [packed.id]: { geminarrUrl: GEMINARR_URL, token: TOKEN, roundMinutes: 1 } } },
    // The browser of the deployment never keeps a password, a profile of a person or a synced copy of itself.
    PasswordManagerEnabled: false,
    BrowserSignin: 0,
    SyncDisabled: true,
    DeveloperToolsAvailability: 2,
    RemoteDebuggingAllowed: false,
    URLBlocklist: ['*'],
    URLAllowlist: [ASSISTANT_ORIGIN],
  }),
);
console.log(`extension ${packed.id} ${packed.version}`);
