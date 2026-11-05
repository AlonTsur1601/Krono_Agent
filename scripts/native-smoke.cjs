// Real WinUI window + UI Automation, SQLite, DPAPI, PTY and WebView2.
// Auth/provider endpoints are local fixtures. No owner account is touched.
const http = require('http')
const fs = require('fs')
const os = require('os')
const path = require('path')
const { spawn } = require('child_process')
const { DatabaseSync } = require('node:sqlite')
const home = fs.mkdtempSync(path.join(os.tmpdir(), 'krono-native-'))
const user = { id: '33333333-3333-4333-8333-333333333333', email: 'native-fixture@example.com' }
const requests = []
let revoked = false
let browserIsolated = false
let localNoTools = false
let localKeyIsolated = false
let localModelBNoTools = false
let sideNoTools = false, sideCancelled = false, sideStreamActive = false
const heldParents = new Set()
const cloudAccepted = new Set()
let keptSideId = ''
function fixtureDb(action) {
  const db = new DatabaseSync(path.join(home, 'databases', user.id + '.db'))
  try { db.exec('PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON'); return action(db) } finally { db.close() }
}
function complete(response, text) {
  response.write('data: ' + JSON.stringify({ type: 'response.output_text.delta', delta: text }) + '\n\n')
  response.end('data: ' + JSON.stringify({ type: 'response.completed', response: { usage: { input_tokens: 1, output_tokens: 1 } } }) + '\n\n')
}
const server = http.createServer((request, response) => {
  const cloud = request.url.match(/^\/cloud\/(azure|xai|groq|mistral|openrouter)\/v1\/(models|chat\/completions)$/)
  if (cloud) {
    const id = cloud[1]
    if (cloud[2] === 'models') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ data: [{ id: id + '-fixture' }] })); return }
    let body = ''; request.on('data', chunk => body += chunk); request.on('end', () => {
      const input = JSON.parse(body)
      if (input.model !== id + '-fixture' || request.headers.authorization !== 'Bearer fixture-' + id + '-key' || input.tools) { response.writeHead(400); response.end('Invalid cloud profile fixture request'); return }
      cloudAccepted.add(id); response.setHeader('Content-Type', 'text/event-stream')
      response.write('data: ' + JSON.stringify({ choices: [{ delta: { content: 'Native ' + id + ' fixture reply.' }, finish_reason: null }] }) + '\n\n')
      response.end('data: ' + JSON.stringify({ choices: [{ delta: {}, finish_reason: 'stop' }] }) + '\n\ndata: [DONE]\n\n')
    })
  }
  else if (request.url === '/cloud-proof') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ accepted: [...cloudAccepted] })) }
  else if (request.url === '/poison-font-sizes') { fixtureDb(db => { db.prepare("INSERT OR REPLACE INTO settings(key,value) VALUES('native.textSize','NaN')").run(); db.prepare("INSERT OR REPLACE INTO settings(key,value) VALUES('native.codeSize','Infinity')").run() }); response.end('ok') }
  else if (request.url === '/font-proof') { const proof = fixtureDb(db => ({ text: db.prepare("SELECT value FROM settings WHERE key='native.textSize'").get()?.value, code: db.prepare("SELECT value FROM settings WHERE key='native.codeSize'").get()?.value })); response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify(proof)) }
  else if (request.url === '/side-proof') {
    const proof = fixtureDb(db => {
      const side = db.prepare('SELECT * FROM side_chats ORDER BY rowid DESC LIMIT 1').get()
      const queued = db.prepare('SELECT count(*) AS n FROM side_transfers').get().n
      const transferred = db.prepare("SELECT count(*) AS n FROM messages WHERE content LIKE 'Context transferred from a side chat:%'").get().n
      let snapshot = false
      if (side) {
        const parent = db.prepare('SELECT role,content FROM messages WHERE chat_id=? ORDER BY created_at,rowid').all(side.parent_chat_id)
        const child = db.prepare('SELECT role,content FROM messages WHERE chat_id=? ORDER BY created_at,rowid').all(side.chat_id)
        snapshot = JSON.stringify(parent) === JSON.stringify(child.slice(0, parent.length))
        if (!keptSideId) keptSideId = side.chat_id
      }
      const kept = keptSideId && !!db.prepare('SELECT id FROM chats WHERE id=?').get(keptSideId) && !db.prepare('SELECT chat_id FROM side_chats WHERE chat_id=?').get(keptSideId)
      return { sideCount: db.prepare('SELECT count(*) AS n FROM side_chats').get().n, sideMessageCount: side ? db.prepare('SELECT count(*) AS n FROM messages WHERE chat_id=?').get(side.chat_id).n : 0, queued, transferred, snapshot, kept, activeSide: side?.chat_id, expiresAt: side?.expires_at }
    })
    response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ ...proof, sideNoTools, sideCancelled, sideStreamActive, parentHeld: heldParents.size > 0 }))
  }
  else if (request.url === '/expire-side') { fixtureDb(db => db.prepare('UPDATE side_chats SET expires_at=?').run(Date.now() - 1)); response.end('ok') }
  else if (request.url === '/release-parent') { for (const held of heldParents) complete(held, 'Native parent continued.'); heldParents.clear(); response.end('ok') }
  else if (request.url === '/local/v1/models') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ data: [{ id: 'local-fixture-model' }] })) }
  else if (request.url === '/local/v1/chat/completions') {
    let body = ''; request.on('data', chunk => body += chunk); request.on('end', () => {
      const input = JSON.parse(body)
      localNoTools = !input.tools && ['local-fixture-model','basic-fixture-model'].includes(input.model) && input.messages[0].content.includes('Tool calling is disabled')
      if (input.model === 'basic-fixture-model') localModelBNoTools = localNoTools
      localKeyIsolated = request.headers.authorization === 'Bearer local'
      response.setHeader('Content-Type', 'text/event-stream')
      response.write('data: ' + JSON.stringify({ choices: [{ delta: { content: 'Native local endpoint reply.' }, finish_reason: null }] }) + '\n\n')
      response.end('data: ' + JSON.stringify({ choices: [{ delta: {}, finish_reason: 'stop' }] }) + '\n\ndata: [DONE]\n\n')
    })
  }
  else if (request.url === '/v1/models') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ object: 'list', data: [{ id: 'native-fixture-model', object: 'model' }] })) }
  else if (request.url === '/v1/responses') {
    let body = ''; request.on('data', chunk => body += chunk); request.on('end', () => {
      const input = JSON.parse(body); requests.push(input)
      fs.writeFileSync(path.join(home, 'provider-fixture-requests.json'), JSON.stringify(requests, null, 2))
      response.setHeader('Content-Type', 'text/event-stream')
      const last = input.input.at(-1)?.content
      if (last === 'NATIVE_PARENT_WAIT') { response.flushHeaders(); heldParents.add(response); response.on('close', () => heldParents.delete(response)); return }
      if (last === 'NATIVE_SIDE_WAIT') { sideStreamActive = true; response.flushHeaders(); response.write('data: ' + JSON.stringify({ type: 'response.output_text.delta', delta: 'Side stream started.' }) + '\n\n'); response.on('close', () => { sideCancelled = true; sideStreamActive = false }); return }
      if (last === 'NATIVE_SIDE_QUESTION') { sideNoTools = !input.tools && input.input[0].content.includes('Native project memory fixture') && input.input.some(message => message.content === 'Native test message'); complete(response, 'Native side reply.'); return }
      if (input.tools?.length && !input.input.some(i => i.type === 'function_call_output')) {
        response.write('data: ' + JSON.stringify({ type: 'response.output_item.done', item: { type: 'function_call', call_id: 'native-file-call', name: 'write_file', arguments: JSON.stringify({ path: path.join(home, 'native-tool-proof.txt'), content: 'native approved write' }) } }) + '\n\n')
      } else response.write('data: ' + JSON.stringify({ type: 'response.output_text.delta', delta: 'Native fixture response.' }) + '\n\n')
      response.end('data: ' + JSON.stringify({ type: 'response.completed', response: { usage: { input_tokens: 1, output_tokens: 1 } } }) + '\n\n')
    })
  } else if (request.url === '/assertions') {
    const firstContext = requests[0]?.input[0]?.content || ''
    const secondContext = requests[1]?.input[0]?.content || ''
    response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ browserIsolated, localNoTools, localKeyIsolated, localModelBNoTools, completions: requests.length, chatHasNoTools: requests.length > 0 && !requests[0].tools, globalOnly: firstContext.includes('Native global memory fixture') && !firstContext.includes('Native project memory fixture'), projectScope: secondContext.includes('Native global memory fixture') && secondContext.includes('Native project memory fixture'), toolWrite: fs.existsSync(path.join(home, 'native-tool-proof.txt')) }))
  }
  else if (request.url === '/revoke') { revoked = true; response.end('ok') }
  else if (request.url.startsWith('/auth/')) {
    response.setHeader('Content-Type', 'application/json')
    let body = ''; request.on('data', chunk => body += chunk)
    request.on('end', () => {
      const input = body ? JSON.parse(body) : {}
      if (input.refresh_token && revoked) { response.writeHead(400); response.end(JSON.stringify({ msg: 'Refresh token revoked' })); return }
      if (input.password && input.password !== 'fixture-password') { response.writeHead(400); response.end(JSON.stringify({ msg: 'Invalid login credentials' })); return }
      if (input.password) revoked = false
      response.end(JSON.stringify({ user, access_token: 'native-fixture-access', refresh_token: 'native-fixture-refresh', expires_in: 3600, token_type: 'bearer' }))
    })
  } else if (request.url.startsWith('/browser-proof')) { const query = new URL(request.url, 'http://localhost').searchParams; browserIsolated = query.get('api') === 'undefined' && query.get('node') === 'undefined'; response.end('ok') }
  else { response.setHeader('Content-Type', 'text/html'); response.end('<title>Native browser fixture</title><h1>WebView2 fixture</h1><script>fetch("/browser-proof?api="+typeof window.api+"&node="+typeof require)</script>') }
})
server.listen(0, '127.0.0.1', () => {
  const root = `http://127.0.0.1:${server.address().port}`
  fs.writeFileSync(path.join(home, 'account-service.json'), JSON.stringify({ url: root, anonKey: 'native-public-fixture' }))
  const exe = process.env.KRONO_SMOKE_EXE || path.resolve('native/Krono.Agent/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/KronoAgent.exe')
  const child = spawn('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('scripts/native-smoke.ps1'), '-Executable', exe, '-Fixture', root, '-ReportDirectory', home], { env: { ...process.env, KRONO_USER_DATA: home, OPENAI_BASE_URL: root + '/v1' }, stdio: 'inherit', windowsHide: true })
  child.on('exit', code => { server.close(); console.log('Native smoke artifacts:', home); process.exitCode = code ?? 1 })
})
