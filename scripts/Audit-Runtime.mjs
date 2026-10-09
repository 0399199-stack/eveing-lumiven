// Parse normal and delay-load PE imports without relying on an installed VC runtime.
import fs from 'node:fs';
import path from 'node:path';
const directory = path.resolve(process.argv[2] || 'artifacts/publish');
const output = process.argv[3] ? path.resolve(process.argv[3]) : null;
const walk = root => fs.readdirSync(root, { withFileTypes: true }).flatMap(entry => {
  const file = path.join(root, entry.name);
  return entry.isDirectory() ? walk(file) : [file];
});
const files = walk(directory);
const packaged = new Set(files.map(file => path.basename(file).toLowerCase()));
function imports(file) {
  const data = fs.readFileSync(file);
  if (data.length < 64 || data.readUInt16LE(0) !== 0x5a4d) return null;
  const pe = data.readUInt32LE(0x3c);
  if (data.readUInt32LE(pe) !== 0x4550) throw new Error(`Invalid PE header: ${file}`);
  const count = data.readUInt16LE(pe + 6), size = data.readUInt16LE(pe + 20);
  const optional = pe + 24, magic = data.readUInt16LE(optional);
  const is64 = magic === 0x20b;
  const directories = optional + (is64 ? 112 : 96);
  const imageBase = is64 ? Number(data.readBigUInt64LE(optional + 24)) : data.readUInt32LE(optional + 28);
  const sections = Array.from({ length: count }, (_, index) => {
    const start = optional + size + index * 40;
    return { virtualSize: data.readUInt32LE(start + 8), rva: data.readUInt32LE(start + 12), size: data.readUInt32LE(start + 16), offset: data.readUInt32LE(start + 20) };
  });
  const offset = rva => {
    const section = sections.find(item => rva >= item.rva && rva < item.rva + Math.max(item.size, item.virtualSize));
    if (!section) throw new Error(`Unmapped import RVA in ${file}`);
    return section.offset + rva - section.rva;
  };
  const name = rva => { const start = offset(rva); const end = data.indexOf(0, start); return data.toString('ascii', start, end).toLowerCase(); };
  const result = new Set();
  for (const [index, width, nameOffset] of [[1, 20, 12], [13, 32, 4]]) {
    const rva = data.readUInt32LE(directories + index * 8);
    if (!rva) continue;
    let descriptor = offset(rva);
    while (data.subarray(descriptor, descriptor + width).some(value => value !== 0)) {
      let nameRva = data.readUInt32LE(descriptor + nameOffset);
      if (index === 13 && !(data.readUInt32LE(descriptor) & 1)) nameRva -= imageBase;
      result.add(name(nameRva));
      descriptor += width;
    }
  }
  return { machine: data.readUInt16LE(pe + 4), imports: [...result].sort() };
}
const runtimePattern = /^(?:vcruntime\d.*|msvcp\d.*|concrt\d.*|vcomp\d.*|libgcc.*|libstdc\+\+.*|libwinpthread.*)\.dll$/i;
const missingRuntime = [], unresolved = [], images = [];
for (const file of files.filter(file => /\.(dll|exe)$/i.test(file))) {
  const image = imports(file);
  if (!image) continue;
  const relative = path.relative(directory, file);
  images.push({ file: relative, ...image });
  for (const dependency of image.imports) {
    if (packaged.has(dependency)) continue;
    if (runtimePattern.test(dependency)) missingRuntime.push({ file: relative, dependency });
    else if (!/^(?:api-ms-|ext-ms-)/.test(dependency) && !fs.existsSync(path.join(process.env.SystemRoot || 'C:/Windows', 'System32', dependency))) unresolved.push({ file: relative, dependency });
  }
}
const result = { scannedImages: images.length, missingRuntime, unresolved, images };
if (output) { fs.mkdirSync(path.dirname(output), { recursive: true }); fs.writeFileSync(output, JSON.stringify(result, null, 2)); }
console.log(JSON.stringify({ scannedImages: images.length, missingRuntime, unresolved }));
if (missingRuntime.length || unresolved.length) process.exitCode = 1;

