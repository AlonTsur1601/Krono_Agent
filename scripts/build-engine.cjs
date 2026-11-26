const { buildSync } = require('esbuild')
const result = buildSync({ entryPoints: ['src/sidecar/index.ts'], bundle: true, platform: 'node', target: 'node24', outfile: 'dist/sidecar/engine.cjs', format: 'cjs', external: ['node-pty'], logLevel: 'warning', metafile: true })
const fs = require('fs')
const path = require('path')
const firebase = buildSync({ entryPoints: ['src/sidecar/firebase-login.cjs'], bundle: true, platform: 'node',
  target: 'node24', outfile: 'dist/sidecar/firebase/firebase-login.cjs', format: 'cjs', logLevel: 'warning', metafile: true,
  alias: { open: './src/sidecar/firebase-browser.cjs' } })
fs.mkdirSync('dist/sidecar/templates', { recursive: true })
for (const template of ['loginSuccess.html', 'loginFailure.html'])
  fs.copyFileSync(path.join('node_modules/firebase-tools/templates', template), path.join('dist/sidecar/templates', template))
fs.copyFileSync(process.execPath, 'dist/sidecar/node.exe')
if (process.platform === 'win32') {
  const cli = 'node_modules/@vercel/vc-native-win32-x64/bin/vercel.exe'
  fs.mkdirSync('dist/sidecar/vercel', { recursive: true })
  fs.copyFileSync(cli, 'dist/sidecar/vercel/vercel.exe')
}
fs.copyFileSync('src/db/schema.sql', 'dist/sidecar/schema.sql')
fs.copyFileSync('src/agent/providers/catalog.json', 'dist/sidecar/providers.json')
const pluginPackages = path.join('dist', 'sidecar', 'plugin-packages')
fs.mkdirSync(pluginPackages, { recursive: true })
for (const { id, type, guidance, tools } of require('../src/agent/tools/plugin-catalog.json')) {
  if (!/^[a-z0-9-]+$/.test(id)) throw new Error('Invalid plugin package ID')
  fs.writeFileSync(path.join(pluginPackages, id + '.json'), JSON.stringify({ id, type, guidance, tools }))
}
for (const item of ['lib', 'prebuilds/win32-x64', 'package.json', 'LICENSE']) {
  const source = path.join('node_modules/node-pty', item)
  if (fs.existsSync(source)) fs.cpSync(source, path.join('dist/sidecar/node_modules/node-pty', item), { recursive: true })
}
const packages = new Set(['node-pty'])
for (const file of [...Object.keys(result.metafile.inputs), ...Object.keys(firebase.metafile.inputs)]) {
  const match = file.match(/^node_modules\/((?:@[^/]+\/)?[^/]+)/)
  if (match) packages.add(match[1])
}
const notices = []
for (const name of [...packages].sort()) {
  const directory = path.join('node_modules', name)
  const license = fs.readdirSync(directory).find(file => /^licen[sc]e(?:\..*)?$/i.test(file))
  if (license) notices.push(name + '\n' + fs.readFileSync(path.join(directory, license), 'utf8'))
}
fs.writeFileSync('dist/sidecar/THIRD_PARTY_NOTICES.txt', notices.join('\n\n--------------------\n\n'))
// Node's official distribution license includes its bundled third-party notices.
async function nodeLicense() {
  const target = 'dist/sidecar/NODE_LICENSE.txt'
  if (fs.existsSync(target) && fs.readFileSync(target, 'utf8').startsWith(process.version + '\n')) return
  const response = await fetch(`https://raw.githubusercontent.com/nodejs/node/${process.version}/LICENSE`)
  if (!response.ok) throw new Error('Cannot retrieve the Node runtime license for redistribution')
  fs.writeFileSync(target, process.version + '\n' + await response.text())
}
nodeLicense().catch(error => { console.error(error.message); process.exitCode = 1 })
async function vercelLicense() {
  if (process.platform !== 'win32') return
  const target = 'dist/sidecar/vercel/LICENSE.txt'
  if (fs.existsSync(target)) return
  const response = await fetch('https://raw.githubusercontent.com/vercel/vercel/main/LICENSE')
  if (!response.ok) throw new Error('Cannot retrieve the Vercel CLI license for redistribution')
  fs.writeFileSync(target, await response.text())
}
vercelLicense().catch(error => { console.error(error.message); process.exitCode = 1 })
