import assert from 'node:assert/strict';
import { createPublicKey, createVerify, generateKeyPairSync } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';
import { inflateRawSync } from 'node:zlib';

import { browserVersion, checkedOrigin, extensionId, pack, writePackage } from '../pack.mjs';

const folder = mkdtempSync(join(tmpdir(), 'geminarr-pack-'));
after(() => rmSync(folder, { recursive: true, force: true }));
const source = join(folder, 'source');
mkdirSync(source);
writeFileSync(join(source, 'manifest.json'), '{ "version": "0.0.0", "host_permissions": ["__ASSISTANT_ORIGIN__/*"] }');
writeFileSync(join(source, 'background.js'), "const ORIGIN = '__ASSISTANT_ORIGIN__';");
writeFileSync(join(source, 'icon.bin'), Buffer.from([1, 2, 3, 4]));
const pem = generateKeyPairSync('rsa', { modulusLength: 2048 }).privateKey.export({ type: 'pkcs8', format: 'pem' });

/** Reads a CRX3 back: the header, the public key, the signature and the files of the archive. */
function open(crx) {
  assert.equal(crx.subarray(0, 4).toString(), 'Cr24');
  assert.equal(crx.readUInt32LE(4), 3);
  const headerLength = crx.readUInt32LE(8);
  const header = crx.subarray(12, 12 + headerLength);
  const archive = crx.subarray(12 + headerLength);
  const fields = (buffer) => {
    const found = {};
    let i = 0;
    const varint = () => {
      let value = 0;
      for (let shift = 0; ; shift += 7) {
        const byte = buffer[i++];
        value += (byte & 127) * 2 ** shift;
        if (byte < 128) return value;
      }
    };
    while (i < buffer.length) {
      const number = Math.floor(varint() / 8);
      const length = varint();
      found[number] = buffer.subarray(i, i + length);
      i += length;
    }
    return found;
  };
  const top = fields(header);
  const proof = fields(top[2]);
  const entries = {};
  const endOfDirectory = archive.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  let at = archive.readUInt32LE(endOfDirectory + 16);
  for (let i = 0; i < archive.readUInt16LE(endOfDirectory + 10); i++) {
    const nameLength = archive.readUInt16LE(at + 28);
    const compressed = archive.readUInt32LE(at + 20);
    const local = archive.readUInt32LE(at + 42);
    const name = archive.subarray(at + 46, at + 46 + nameLength).toString();
    const start = local + 30 + archive.readUInt16LE(local + 26) + archive.readUInt16LE(local + 28);
    entries[name] = inflateRawSync(archive.subarray(start, start + compressed));
    at += 46 + nameLength;
  }
  return { publicKey: proof[1], signature: proof[2], signedData: top[10000], archive, entries };
}

describe('browserVersion', () => {
  it('keeps the three numbers of a release and drops what follows', () => {
    assert.equal(browserVersion('1.2.3'), '1.2.3');
    assert.equal(browserVersion('1.2.3-rc.1+abc'), '1.2.3');
    assert.equal(browserVersion('0.0.0-dev'), '0.0.0');
  });

  it('keeps the number of the run of a release, so that each build is a version above the last', () => {
    assert.equal(browserVersion('1.2.3.45'), '1.2.3.45');
  });

  it('refuses what does not start with three numbers', () => {
    for (const bad of ['dev', '1.2', '', undefined, 'v1.2.3', '1.2.3.4.5', '1.2.70000'])
      assert.throws(() => browserVersion(bad));
  });
});

describe('checkedOrigin', () => {
  it('gives the origin of an address with no path', () => {
    assert.equal(checkedOrigin('https://assistant.example/'), 'https://assistant.example');
    assert.equal(checkedOrigin('http://assistant:8081'), 'http://assistant:8081');
  });

  it('refuses a path, a query, a login and another scheme', () => {
    for (const bad of [
      'https://a.example/x',
      'https://a.example/?q=1',
      'https://u:p@a.example',
      'ftp://a.example',
      'not a url',
    ])
      assert.throws(() => checkedOrigin(bad));
  });
});

describe('pack', () => {
  const packed = pack({ source, version: '1.2.3-rc.1', origin: 'https://assistant.example', keyPem: pem });
  const crx = open(packed.crx);

  it('names the file and the version, and the id that the public key gives', () => {
    assert.equal(packed.file, 'geminarr-reader-1.2.3.crx');
    assert.equal(packed.version, '1.2.3');
    assert.match(packed.id, /^[a-p]{32}$/);
    assert.equal(packed.id, extensionId(createPublicKey(pem).export({ type: 'spki', format: 'der' })).id);
  });

  it('writes the origin and the version into the files that ship, and copies the others as they are', () => {
    assert.deepEqual(JSON.parse(crx.entries['manifest.json']), {
      version: '1.2.3',
      host_permissions: ['https://assistant.example/*'],
    });
    assert.equal(crx.entries['background.js'].toString(), "const ORIGIN = 'https://assistant.example';");
    assert.deepEqual([...crx.entries['icon.bin']], [1, 2, 3, 4]);
  });

  it('is signed by the key, over the archive that it holds', () => {
    const verifier = createVerify('sha256');
    verifier.update(Buffer.from('CRX3 SignedData\0'));
    const length = Buffer.alloc(4);
    length.writeUInt32LE(crx.signedData.length);
    verifier.update(length);
    verifier.update(crx.signedData);
    verifier.update(crx.archive);
    assert.ok(verifier.verify(createPublicKey({ key: crx.publicKey, format: 'der', type: 'spki' }), crx.signature));
  });

  it('is the same bytes for the same key, source and version', () => {
    // RSA PKCS#1 v1.5 signatures are deterministic: a rebuild of a release is the same file.
    assert.deepEqual(
      pack({ source, version: '1.2.3', origin: 'https://assistant.example', keyPem: pem }).crx,
      packed.crx,
    );
  });

  it('is another extension with another key, and with no key it makes one', () => {
    const other = pack({ source, version: '1.2.3' });
    assert.notEqual(other.id, packed.id);
    assert.equal(pack({ source, version: '1.2.3' }).id === other.id, false, 'an ephemeral key is new each time');
  });

  it('writes the file and its description for Geminarr to read', () => {
    const out = join(folder, 'out');
    writePackage(out, packed);

    assert.deepEqual(JSON.parse(readFileSync(join(out, 'extension.json'), 'utf8')), {
      id: packed.id,
      version: '1.2.3',
      file: 'geminarr-reader-1.2.3.crx',
    });
    assert.deepEqual(readFileSync(join(out, packed.file)), packed.crx);
  });
});
