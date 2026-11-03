// node-pty ships N-API prebuilds on Windows/macOS. Rebuilding it unnecessarily
// requires optional VS Spectre libraries. SQLite does require Electron's ABI.
async function main() {
  const { rebuild } = await import('@electron/rebuild')
  await rebuild({
    buildPath: process.cwd(),
    electronVersion: require('electron/package.json').version,
    onlyModules: ['better-sqlite3'],
  })
}
main().catch(error => { console.error(error.message); process.exitCode = 1 })
