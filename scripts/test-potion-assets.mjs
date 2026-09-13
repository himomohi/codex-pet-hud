import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { runInNewContext } from 'node:vm';

const root = new URL('../', import.meta.url);
const schema = JSON.parse(readFileSync(new URL('shared/contracts/overlay-settings.schema.json', root)));
const styles = schema.properties.potionStyle.enum;
assert.equal(schema.properties.potionStyle.default, 'classic');
assert.equal(styles.length, 6);
assert.equal(new Set(styles).size, 6);
const directories = [
  new URL('shared/settings/potions/', root),
  new URL('platforms/windows/CodexPetLimitRings.Windows/Assets/Potions/', root),
];
const names = styles.filter(id => id !== 'classic').flatMap(id => ['', '-frame', '-mask'].map(suffix => `${id}${suffix}.png`)).sort();
const maskContext = { window: {} };
runInNewContext(readFileSync(new URL('shared/settings/potion-masks.js', root), 'utf8'), maskContext, { timeout: 1000 });
assert.deepEqual(Object.keys(maskContext.window.potionMaskImages).sort(), styles.filter(id => id !== 'classic').sort());
for (const [id, uri] of Object.entries(maskContext.window.potionMaskImages)) {
  assert.ok(uri.startsWith('data:image/png;base64,'));
  assert.deepEqual(Buffer.from(uri.split(',')[1], 'base64'), readFileSync(new URL(`${id}-mask.png`, directories[0])), `${id}: 로컬 HTML 마스크 원본 일치`);
}
for (const directory of directories) {
  assert.deepEqual(readdirSync(directory).filter(name => name.endsWith('.png')).sort(), names, fileURLToPath(directory));
}
for (const name of names) {
  const [shared, windows] = directories.map(directory => readFileSync(new URL(name, directory)));
  assert.deepEqual(shared, windows, `${name}: 두 플랫폼 이미지가 다름`);
  assert.equal(shared.subarray(0, 8).toString('hex'), '89504e470d0a1a0a', `${name}: PNG 형식`);
  assert.equal(shared.readUInt32BE(16), 96, `${name}: 가로 크기`);
  assert.equal(shared.readUInt32BE(20), 96, `${name}: 세로 크기`);
  assert.equal(shared[25], 6, `${name}: 투명도 포함 RGBA`);
}
console.log('포션 계약과 macOS/Windows 15개 PNG 일치 검증 통과');
