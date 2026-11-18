// Packs the extension (extension/) into a signed CRX3 with Node alone: no Chrome, no package to install, so the image build stays small and the result is the same everywhere (the zip is reproducible: sorted names, a fixed date).
// The extension id is a function of the public key, so the key is what makes the extension the same extension from one version to the next (docs/adr/0015). Without a key an ephemeral one is made, for a build that
// publishes nothing: its id is new each time.
//   node pack.mjs --source extension --out _output/extension --version 1.2.3 [--key key.pem] [--origin https://gemini.google.com]
import { createHash, createPrivateKey, createPublicKey, createSign, generateKeyPairSync } from 'node:crypto';
import { mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { pathToFileURL } from 'node:url';
import { crc32, deflateRawSync } from 'node:zlib';

export const DEFAULT_ORIGIN = 'https://gemini.google.com';
const ORIGIN_PLACEHOLDER = '__ASSISTANT_ORIGIN__';
const TEXT_FILES = /\.(json|js)$/;

/** The version that a browser accepts (up to four numbers, each below 65536) from the one of a build: "1.2.3.45" is kept, "1.2.3-rc.1" and "0.0.0-dev" give "1.2.3" and "0.0.0". */
export function browserVersion(version) {
  const match = /^(\d{1,5})\.(\d{1,5})\.(\d{1,5})(?:\.(\d{1,5}))?(?![\d.])/.exec(version ?? '');
  const numbers = match?.slice(1).filter((part) => part !== undefined);
  if (!numbers || numbers.some((part) => Number(part) > 65_535))
    throw new Error(`The version "${version}" does not start with three numbers below 65536.`);
  return numbers.join('.');
}

/** The origin of the assistant, as a browser writes a match pattern: a scheme, a host and perhaps a port, with no path. */
export function checkedOrigin(origin) {
  const url = new URL(origin);
  if (!['https:', 'http:'].includes(url.protocol) || url.pathname !== '/' || url.search || url.hash || url.username) {
    throw new Error(`The origin "${origin}" must be a scheme, a host and perhaps a port.`);
  }
  return url.origin;
}

const files = (dir) =>
  readdirSync(dir)
    .sort()
    .flatMap((name) => (statSync(join(dir, name)).isDirectory() ? files(join(dir, name)) : [join(dir, name)]));

/** The files of the folder, as they ship: the origin of the assistant and the version are written in. */
function sourceFiles(folder, version, origin) {
  return files(folder).map((path) => {
    let data = readFileSync(path);
    if (TEXT_FILES.test(path)) {
      let text = data.toString('utf8').replaceAll(ORIGIN_PLACEHOLDER, origin);
      if (relative(folder, path) === 'manifest.json')
        text = text.replace(/"version": "[^"]*"/, `"version": "${version}"`);
      data = Buffer.from(text);
    }
    return { name: Buffer.from(relative(folder, path).split('\\').join('/')), data };
  });
}

function zip(entries) {
  const records = [];
  const parts = [];
  let offset = 0;
  for (const { name, data } of entries) {
    const compressed = deflateRawSync(data, { level: 9 });
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(8, 8);
    local.writeUInt16LE(0x21, 12);
    local.writeUInt32LE(crc >>> 0, 14);
    local.writeUInt32LE(compressed.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(name.length, 26);
    parts.push(local, name, compressed);
    records.push({ name, crc, compressed: compressed.length, size: data.length, offset });
    offset += local.length + name.length + compressed.length;
  }
  const central = records.map((record) => {
    const header = Buffer.alloc(46);
    header.writeUInt32LE(0x02014b50, 0);
    header.writeUInt16LE(20, 4);
    header.writeUInt16LE(20, 6);
    header.writeUInt16LE(8, 10);
    header.writeUInt16LE(0x21, 14);
    header.writeUInt32LE(record.crc >>> 0, 16);
    header.writeUInt32LE(record.compressed, 20);
    header.writeUInt32LE(record.size, 24);
    header.writeUInt16LE(record.name.length, 28);
    header.writeUInt32LE(record.offset, 42);
    return Buffer.concat([header, record.name]);
  });
  const directory = Buffer.concat(central);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(records.length, 8);
  end.writeUInt16LE(records.length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...parts, directory, end]);
}

// Protobuf by hand: the few fields of the CRX3 header.
const varint = (number) => {
  const bytes = [];
  let rest = number;
  while (rest > 127) {
    bytes.push((rest % 128) | 128);
    rest = Math.floor(rest / 128);
  }
  bytes.push(rest);
  return Buffer.from(bytes);
};
const field = (number, buffer) => Buffer.concat([varint(number * 8 + 2), varint(buffer.length), buffer]);

/** The id that a browser derives from a public key: the first sixteen bytes of its hash, each nibble written as a letter from a to p. */
export function extensionId(publicDer) {
  const crxId = createHash('sha256').update(publicDer).digest().subarray(0, 16);
  return { crxId, id: [...crxId].map((byte) => String.fromCodePoint(97 + (byte >> 4), 97 + (byte & 15))).join('') };
}

/** Packs the folder and returns the file and what describes it. The key is a PEM (PKCS#8, RSA 2048): none makes an ephemeral one. */
export function pack({ source, version, origin = DEFAULT_ORIGIN, keyPem }) {
  const shippedOrigin = checkedOrigin(origin);
  const shippedVersion = browserVersion(version);
  const privateKey = keyPem
    ? createPrivateKey(keyPem)
    : createPrivateKey(
        generateKeyPairSync('rsa', { modulusLength: 2048 }).privateKey.export({ type: 'pkcs8', format: 'pem' }),
      );
  const publicDer = createPublicKey(privateKey).export({ type: 'spki', format: 'der' });
  const { crxId, id } = extensionId(publicDer);
  const archive = zip(sourceFiles(source, shippedVersion, shippedOrigin));
  const signedData = field(1, crxId);
  const signer = createSign('sha256');
  signer.update(Buffer.from('CRX3 SignedData\0'));
  const length = Buffer.alloc(4);
  length.writeUInt32LE(signedData.length);
  signer.update(length);
  signer.update(signedData);
  signer.update(archive);
  const header = Buffer.concat([
    field(2, Buffer.concat([field(1, publicDer), field(2, signer.sign(privateKey))])),
    field(10000, signedData),
  ]);
  const magic = Buffer.alloc(12);
  magic.write('Cr24', 0);
  magic.writeUInt32LE(3, 4);
  magic.writeUInt32LE(header.length, 8);
  return {
    crx: Buffer.concat([magic, header, archive]),
    id,
    version: shippedVersion,
    file: `geminarr-reader-${shippedVersion}.crx`,
  };
}

/** Writes the package and its description (`extension.json`, what Geminarr reads) into a folder. */
export function writePackage(out, packed) {
  mkdirSync(out, { recursive: true });
  writeFileSync(join(out, packed.file), packed.crx);
  writeFileSync(
    join(out, 'extension.json'),
    `${JSON.stringify({ id: packed.id, version: packed.version, file: packed.file }, null, 2)}\n`,
  );
}

function options(argv) {
  const values = {};
  for (let i = 0; i < argv.length; i += 2) {
    if (!argv[i].startsWith('--') || argv[i + 1] === undefined) throw new Error(`Unexpected argument "${argv[i]}".`);
    values[argv[i].slice(2)] = argv[i + 1];
  }
  return values;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const values = options(process.argv.slice(2));
  if (!values.source || !values.out || !values.version)
    throw new Error(
      'Usage: pack.mjs --source <folder> --out <folder> --version <x.y.z> [--key <pem file>] [--origin <url>]',
    );
  const packed = pack({
    source: values.source,
    version: values.version,
    origin: values.origin,
    keyPem: values.key ? readFileSync(values.key, 'utf8') : undefined,
  });
  writePackage(values.out, packed);
  console.log(
    `${packed.file}: extension id ${packed.id}${values.key ? '' : ' (ephemeral key: nothing signed by it is the extension of a release)'}`,
  );
}
