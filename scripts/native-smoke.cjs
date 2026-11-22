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
let signupConfirmed = false
let browserIsolated = false
let browserPointerReached = false
let browserSelection = ''
let localNoTools = false
let localKeyIsolated = false
let localModelBNoTools = false
let sideNoTools = false, sideCancelled = false, sideStreamActive = false
let commandRan = false
let attachmentAccepted = false
const heldParents = new Set()
const cloudAccepted = new Set()
let keptSideId = ''
const syncRows = new Map()
const syncKeys = { projects:'id', chats:'id', messages:'id', memories:'id', account_settings:'key', game_scores:'id', game_saves:'game_id' }
function fixtureDb(action) {
  const db = new DatabaseSync(path.join(home, 'databases', user.id + '.db'))
  try { db.exec('PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON'); return action(db) } finally { db.close() }
}
function complete(response, text) {
  response.write('data: ' + JSON.stringify({ type: 'response.output_text.delta', delta: text }) + '\n\n')
  response.end('data: ' + JSON.stringify({ type: 'response.completed', response: { usage: { input_tokens: 1, output_tokens: 1 } } }) + '\n\n')
}
let agentBrowserRan = false, profilesSeparate = false, agentCookiePresent = false
let trayResponse = null
const server = http.createServer((request, response) => {
  const rest = request.url.match(/^\/rest\/v1\/([a-z_]+)\?/)
  if (rest) {
    const table = rest[1]; const key = syncKeys[table]; const filters = new URL(request.url, 'http://localhost').searchParams
    const json = data => { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify(data)) }
    if (!key || request.headers.authorization !== 'Bearer native-fixture-access' || request.headers.apikey !== 'native-public-fixture') { response.writeHead(403); json({ error:'fixture account mismatch' }); return }
    const value = name => { const filter = filters.get(name)?.slice(3) || ''; return filter.startsWith('"') ? JSON.parse(filter) : filter }
    if (request.method === 'GET') {
      if (value('account_id') !== user.id) { response.writeHead(403); json([]); return }
      const cursor = value(key)
      json([...syncRows.entries()].filter(([id, row]) => id.startsWith(table + ':') && row[key] > cursor).map(([,row]) => row).sort((a,b) => a[key] > b[key] ? 1 : -1).slice(0,200)); return
    }
    let body = ''; request.on('data', chunk => body += chunk); request.on('end', () => {
      const row = JSON.parse(body); const id = table + ':' + row[key]; const existing = syncRows.get(id)
      if (row.account_id !== user.id) { response.writeHead(403); json([]); return }
      if (request.method === 'POST' && existing || request.method === 'PATCH' && (!existing || String(existing.version) !== value('version') || value(key) !== row[key] || value('account_id') !== user.id)) { json([]); return }
      syncRows.set(id,row); json([row])
    }); return
  }
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
  else if (request.url === '/sync-proof') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ memory: syncRows.get('memories:global')?.content, games: [...syncRows.keys()].filter(key => key.startsWith('game_saves:')).length, sideLeaked: [...syncRows.values()].some(row => row.name?.startsWith('Side · ') && row.name?.includes('expire')) })) }
  else if (request.url === '/seed-memory') {
    fixtureDb(db => {
      const project = db.prepare("SELECT id FROM projects WHERE name='Native fixture project'").get().id
      const insert = db.prepare('INSERT INTO memories(id,account_id,project_id,content,created_at,updated_at) VALUES(?,?,?,?,?,?)')
      insert.run('global',user.id,null,'Native global memory fixture',Date.now(),Date.now())
      insert.run('project:'+project,user.id,project,'Native project memory fixture',Date.now(),Date.now())
    })
    response.end('ok')
  }
  else if (request.url === '/edit-local-memory') {
    fixtureDb(db => db.prepare("UPDATE memories SET content=?,updated_at=? WHERE id='global'").run('Local fixture memory edit',Date.now()))
    response.end('ok')
  }
  else if (request.url === '/local-memory') {
    response.setHeader('Content-Type','application/json')
    response.end(JSON.stringify(fixtureDb(db => db.prepare("SELECT content FROM memories WHERE id='global'").get())))
  }
  else if (request.url === '/sync-remote-memory') { const memory = syncRows.get('memories:global'); memory.content = 'Cloud fixture memory'; memory.version++; response.end('ok') }
  else if (request.url === '/schedule-proof') {
    const proof = fixtureDb(db => ({ tasks: db.prepare('SELECT name FROM scheduled_tasks').all().map(task => task.name), runs: db.prepare('SELECT state,chat_id FROM scheduled_runs').all(), response: db.prepare("SELECT content FROM messages WHERE role='assistant' AND content='Native scheduled result.'").get()?.content })); response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify(proof))
  }
  else if (request.url === '/attachment-proof') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ accepted: attachmentAccepted })) }
  else if (request.url === '/project-folder-proof') {
    const directory = fixtureDb(db => db.prepare(
      'SELECT project_directories.path FROM project_directories JOIN projects ON projects.id = project_directories.project_id WHERE projects.name = ?'
    ).get('Native fixture project'))
    response.setHeader('Content-Type', 'application/json')
    response.end(JSON.stringify(directory ?? {}))
  }
  else if (request.url === '/chat-project-proof') {
    const project = fixtureDb(db => db.prepare(
      'SELECT projects.name FROM chats JOIN projects ON projects.id = chats.project_id WHERE chats.name = ? AND chats.deleted_at IS NULL'
    ).get('Native test message')?.name)
    response.setHeader('Content-Type', 'application/json')
    response.end(JSON.stringify({ project }))
  }
  else if (request.url === '/game-proof') { const saves = fixtureDb(db => db.prepare('SELECT game_id,state FROM game_saves WHERE account_id=? ORDER BY game_id').all(user.id).map(row => ({ game: row.game_id, state: JSON.parse(row.state) }))); response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ saves })) }
  else if (request.url === '/local-reply-proof') { const count = fixtureDb(db => db.prepare("SELECT count(*) AS n FROM messages WHERE role='assistant' AND content='Native local endpoint reply.'").get().n); response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ count })) }
  else if (request.url === '/corrupt-game') { fixtureDb(db => db.prepare("UPDATE game_saves SET state='{}' WHERE game_id='Snake'").run()); response.end('ok') }
  else if (request.url === '/odd-color-round-20' && request.method === 'POST') {
    fixtureDb(db => db.prepare("UPDATE game_saves SET state=json_set(state, '$.OddRound', 20, '$.Score', 1900) WHERE game_id='Odd Color'").run())
    response.end('ok')
  }
  else if (request.url === '/jigsaw-endgame' && request.method === 'POST') {
    fixtureDb(db => {
      const row = db.prepare("SELECT id,state FROM game_saves WHERE game_id='Jigsaw'").get()
      const state = JSON.parse(row.state)
      for (let i = 0; i < 16; i++) state.Board[i] = i
      state.Board[14] = 15; state.Board[15] = 14; state.Ended = false
      db.prepare('UPDATE game_saves SET state=? WHERE id=?').run(JSON.stringify(state), row.id)
    }); response.end('ok')
  }
  else if (request.url === '/solitaire-endgame') {
    fixtureDb(db => {
      const row = db.prepare("SELECT id,state FROM game_saves WHERE game_id='Solitaire'").get()
      const state = JSON.parse(row.state); state.Score = 510
      state.Cards = { Stock: [], Waste: [12], Tableau: [[],[],[],[],[],[],[]], FaceDown: [0,0,0,0,0,0,0], Foundations: [12,13,13,13], SelectedKind: -1 }
      db.prepare('UPDATE game_saves SET state=? WHERE id=?').run(JSON.stringify(state), row.id)
    }); response.end('ok')
  }
  else if (request.url === '/poison-font-sizes') { fixtureDb(db => { db.prepare("INSERT OR REPLACE INTO settings(key,value) VALUES('native.textSize','NaN')").run(); db.prepare("INSERT OR REPLACE INTO settings(key,value) VALUES('native.codeSize','Infinity')").run() }); response.end('ok') }
  else if (request.url === '/font-proof') { const proof = fixtureDb(db => ({ text: db.prepare("SELECT value FROM settings WHERE key='native.textSize'").get()?.value, code: db.prepare("SELECT value FROM settings WHERE key='native.codeSize'").get()?.value, codeFamily: db.prepare("SELECT value FROM settings WHERE key='native.codeFont'").get()?.value })); response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify(proof)) }
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
  else if (request.url === '/local/v1/models') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ data: ['local-fixture-model', 'basic-fixture-model', 'native-keyboard-model'].map(id => ({ id })) })) }
  else if (request.url === '/local/v1/chat/completions') {
    let body = ''; request.on('data', chunk => body += chunk); request.on('end', () => {
      const input = JSON.parse(body)
      localNoTools = !input.tools && ['local-fixture-model','basic-fixture-model'].includes(input.model) && input.messages[0].content.includes('Tool calling is disabled')
      if (input.model === 'basic-fixture-model') localModelBNoTools = localNoTools
      localKeyIsolated = request.headers.authorization === 'Bearer fixture-local-placeholder'
      response.setHeader('Content-Type', 'text/event-stream')
      const event = (content, finish = null) => 'data: ' + JSON.stringify({ choices: [{ delta: { content }, finish_reason: finish }] }) + '\n\n'
      response.end(event('Native local endpoint ') + event('reply.') + event('', 'stop') + 'data: [DONE]\n\n')
    })
  }
  else if (request.url === '/v1/models') {
    response.setHeader('Content-Type', 'application/json')
    response.end(JSON.stringify({ object: 'list', data: ['gpt-native-fixture-model', 'gpt-5-mini'].map(id => ({ id, object: 'model' })) }))
  }
  else if (request.url === '/seed-chat-history' && request.method === 'POST') {
    fixtureDb(db => {
      const { id } = db.prepare('SELECT id FROM chats WHERE name = ?').get('Native test message')
      const insert = db.prepare('INSERT INTO messages(id,chat_id,role,content,created_at) VALUES(?,?,?,?,?)')
      const start = Date.now() + 60000
      for (let i = 0; i < 24; i++) {
        insert.run('fixture-history-' + i, id, i % 2 ? 'assistant' : 'user', 'Fixture history message ' + i, start + i)
      }
    })
    response.end('ok')
  }
  else if (request.url === '/reasoning-proof') {
    response.setHeader('Content-Type', 'application/json')
    const request = requests.find(request => request.model === 'gpt-5-mini' && request.reasoning?.effort === 'high')
    const history = request?.input.filter(message => message.role !== 'system') ?? []
    response.end(JSON.stringify({ accepted: Boolean(request), history: history.map(message => message.content) }))
  }
  else if (request.url === '/v1/responses') {
    let body = ''; request.on('data', chunk => body += chunk); request.on('end', () => {
      const input = JSON.parse(body); requests.push(input)
      fs.writeFileSync(path.join(home, 'provider-fixture-requests.json'), JSON.stringify(requests, null, 2))
      response.setHeader('Content-Type', 'text/event-stream')
      const last = input.input.at(-1)?.content
      const lastUserContent = input.input.filter(message => message.role === 'user').at(-1)?.content
      const lastUser = Array.isArray(lastUserContent)
        ? lastUserContent.filter(part => part.type === 'input_text').map(part => part.text).join('\n')
        : lastUserContent
      if (lastUser?.startsWith('Use the selected fixture file')) {
        attachmentAccepted = !input.tools && lastUser.includes('fixture-attachment.txt') && lastUser.includes('ATTACHMENT_PROOF_TEXT \u05e9\u05dc\u05d5\u05dd') && !lastUser.includes(home)
        complete(response, 'Native attachment fixture result.'); return
      }
      if (lastUser === 'NATIVE_PLUGIN_PROOF') {
        const result = input.input.find(message => message.type === 'function_call_output' && message.call_id === 'native-node-plugin')
        if (!result) {
          const guidance = input.input.find(message => message.role === 'system')?.content
          if (!input.tools?.some(tool => tool.name === 'node_inspect_package') || input.tools.some(tool => tool.name.startsWith('github_')) ||
              !String(guidance).includes('Installed skill packs') || !String(guidance).includes('nearest tsconfig')) {
            complete(response, 'Unexpected native plugin tool list.'); return
          }
          response.write('data: ' + JSON.stringify({ type: 'response.output_item.done', item: { type: 'function_call', call_id: 'native-node-plugin', name: 'node_inspect_package', arguments: '{}' } }) + '\n\n')
          response.end('data: ' + JSON.stringify({ type: 'response.completed', response: {} }) + '\n\n')
        } else {
          const output = JSON.parse(result.output)
          const manifest = output.success && JSON.parse(output.output)
          complete(response, manifest?.name === 'native-plugin-fixture' && manifest.scripts.includes('test') ? 'Native Node plugin verified.' : 'The native Node plugin returned the wrong manifest.')
        }
        return
      }
      if (lastUser === 'NATIVE_SCHEDULE_PROOF') {
        if (!input.tools?.some(tool => tool.name === 'read_file')) { complete(response, 'Scheduled task did not default to Agent mode.'); return }
        complete(response, 'Native scheduled result.'); return
      }
      if (lastUser === 'NATIVE_COMMAND_PROOF') {
        const result = input.input.find(message => message.type === 'function_call_output' && message.call_id === 'native-command-call')
        if (!result) {
          const proof = path.join(home, 'native-command-proof.txt').replace(/'/g, "''")
          response.write('data: ' + JSON.stringify({ type: 'response.output_item.done', item: { type: 'function_call', call_id: 'native-command-call', name: 'run_command', arguments: JSON.stringify({ command: `[IO.File]::WriteAllText('${proof}','native command proof'); Write-Output (6*7)`, cwd: home, timeout_ms: 10000 }) } }) + '\n\n')
          response.end('data: ' + JSON.stringify({ type: 'response.completed', response: {} }) + '\n\n')
        } else {
          const output = JSON.parse(result.output)
          commandRan = output.success && output.output.includes('42') && fs.readFileSync(path.join(home, 'native-command-proof.txt'), 'utf8') === 'native command proof'
          complete(response, 'Native command fixture response.')
        }
        return
      }
      if (lastUser === 'NATIVE_BROWSER_PROOF') {
        const output = id => input.input.find(message => message.type === 'function_call_output' && message.call_id === id)?.output
        const visit = output('native-browser-visit'), follow = output('native-browser-follow')
        if (follow) {
          const result = JSON.parse(follow)
          agentBrowserRan = result.success && JSON.parse(result.output).text.includes('AGENT_BROWSER_NEXT_PAGE')
          complete(response, agentBrowserRan ? 'Native agent browser verified.' : 'Native agent browser failed.')
          return
        }
        if (!input.tools?.some(tool => tool.name === 'browse_page' && tool.parameters?.type === 'object') || visit && !JSON.parse(visit).success) {
          complete(response, 'Native agent browser unavailable.'); return
        }
        const url = 'http://127.0.0.1:' + server.address().port + '/agent-browser'
        const item = visit
          ? { type: 'function_call', call_id: 'native-browser-follow', name: 'browse_page', arguments: JSON.stringify({ action: 'follow', link: 'Open next fixture' }) }
          : { type: 'function_call', call_id: 'native-browser-visit', name: 'browse_page', arguments: JSON.stringify({ action: 'visit', url }) }
        response.write('data: ' + JSON.stringify({ type: 'response.output_item.done', item }) + '\n\n')
        response.end('data: ' + JSON.stringify({ type: 'response.completed', response: {} }) + '\n\n')
        return
      }
      if (lastUser === 'NATIVE_TRAY_PROOF') {
        response.flushHeaders()
        trayResponse = response
        response.on('close', () => { if (trayResponse === response) trayResponse = null })
        return
      }
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
    response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ browserIsolated, localNoTools, localKeyIsolated, localModelBNoTools, commandRan, completions: requests.length, chatHasNoTools: requests.length > 0 && !requests[0].tools, globalOnly: firstContext.includes('Native global memory fixture') && !firstContext.includes('Native project memory fixture'), projectScope: secondContext.includes('Native global memory fixture') && secondContext.includes('Native project memory fixture'), toolWrite: fs.existsSync(path.join(home, 'native-tool-proof.txt')) }))
  }
  else if (request.url === '/revoke') { revoked = true; response.end('ok') }
  else if (request.url === '/confirm-signup' && request.method === 'POST') { signupConfirmed = true; response.end('ok') }
  else if (request.url.startsWith('/auth/')) {
    response.setHeader('Content-Type', 'application/json')
    let body = ''; request.on('data', chunk => body += chunk)
    request.on('end', () => {
      const input = body ? JSON.parse(body) : {}
      if (input.email === 'pending-fixture@example.com') {
        if (request.url.startsWith('/auth/v1/signup')) {
          setTimeout(() => response.end(JSON.stringify({ user })), 800)
          return
        }
        if (request.url.startsWith('/auth/v1/resend') && input.type === 'signup') {
          response.end('{}')
          return
        }
        if (!signupConfirmed) {
          response.writeHead(400)
          response.end(JSON.stringify({ error_code: 'email_not_confirmed', msg: 'Email not confirmed' }))
          return
        }
      }
      if (input.refresh_token && revoked) { response.writeHead(400); response.end(JSON.stringify({ msg: 'Refresh token revoked' })); return }
      if (input.password && input.password !== 'fixture-password') { response.writeHead(400); response.end(JSON.stringify({ msg: 'Invalid login credentials' })); return }
      if (input.password) revoked = false
      response.end(JSON.stringify({ user, access_token: 'native-fixture-access', refresh_token: 'native-fixture-refresh', expires_in: 3600, token_type: 'bearer' }))
    })
  } else if (request.url.startsWith('/browser-proof')) { const query = new URL(request.url, 'http://localhost').searchParams; browserIsolated = query.get('api') === 'undefined' && query.get('node') === 'undefined'; response.end('ok') }
  else if (request.url === '/browser-pointer') { browserPointerReached = true; response.end('ok') }
  else if (request.url === '/browser-pointer-proof') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ browserPointerReached })) }
  else if (request.url === '/agent-browser-proof') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ agentBrowserRan, profilesSeparate, agentCookiePresent })) }
  else if (request.url === '/tray-proof') {
    const completed = fixtureDb(db => !!db.prepare("SELECT id FROM messages WHERE role='assistant' AND content='Agent response while hidden.'").get())
    response.setHeader('Content-Type', 'application/json')
    response.end(JSON.stringify({ working: !!trayResponse, completed }))
  }
  else if (request.url === '/finish-tray-proof' && request.method === 'POST') {
    if (trayResponse) complete(trayResponse, 'Agent response while hidden.')
    response.end('ok')
  }
  else if (request.url === '/agent-browser') {
    profilesSeparate = !request.headers.cookie?.includes('KronoManual=1')
    response.writeHead(200, { 'Content-Type': 'text/html', 'Set-Cookie': 'KronoAgent=1; Path=/; SameSite=Lax' })
    response.end('<title>Native agent browser</title><a href="/agent-browser-next">Open next fixture</a>')
  }
  else if (request.url === '/agent-browser-next') {
    agentCookiePresent = request.headers.cookie?.includes('KronoAgent=1') === true
    response.writeHead(200, { 'Content-Type': 'text/html' })
    response.end('<title>Native agent browser next</title><h1>AGENT_BROWSER_NEXT_PAGE</h1>')
  }
  else if (request.url.startsWith('/browser-selection?')) { browserSelection = new URL(request.url, 'http://localhost').searchParams.get('text'); response.end('ok') }
  else if (request.url === '/browser-selection') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ browserSelection })) }
  else { response.setHeader('Content-Type', 'text/html'); if (request.url === '/probe') response.setHeader('Set-Cookie', 'KronoManual=1; Path=/; SameSite=Lax'); response.end('<title>' + (request.url === '/home' ? 'Native browser home' : 'Native browser fixture') + '</title><h1>WebView2 fixture</h1><a id="fixture-link" href="/fixture-destination" style="position:fixed;inset:80px 0 0;display:block">Fixture link for hover</a><script>fetch("/browser-proof?api="+typeof window.api+"&node="+typeof require);document.getElementById("fixture-link").addEventListener("pointerenter",()=>fetch("/browser-pointer"));document.addEventListener("selectionchange",()=>fetch("/browser-selection?text="+encodeURIComponent(String(getSelection()))))</script>') }
})
const localDiscovery = process.env.KRONO_SMOKE_SCOPE === 'local-model-ui'
const modelServer = localDiscovery ? http.createServer((request, response) => {
  if (request.url === '/v1/models') {
    response.setHeader('Content-Type', 'application/json')
    response.end(JSON.stringify({ data: [{ id: 'native-discovered-model:latest' }, { id: 'alpha-discovered-model:latest' }] }))
  } else if (request.url === '/v1/chat/completions') {
    let body = ''
    request.on('data', chunk => { body += chunk })
    request.on('end', () => {
      if (JSON.parse(body).model !== 'native-discovered-model:latest') { response.writeHead(400).end(); return }
      response.writeHead(200, { 'Content-Type': 'text/event-stream' })
      response.end('data: {"choices":[{"delta":{"content":"Automatically discovered local model replied."},"finish_reason":"stop"}]}\n\ndata: [DONE]\n\n')
    })
  } else response.writeHead(404).end()
}) : null

function startSmoke() {
server.listen(0, '127.0.0.1', () => {
  const root = `http://127.0.0.1:${server.address().port}`
  fs.writeFileSync(path.join(home, 'account-service.json'), JSON.stringify({ url: root, anonKey: 'native-public-fixture' }))
  const exe = process.env.KRONO_SMOKE_EXE || path.resolve('native/Krono.Agent/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/KronoAgent.exe')
  const closeWindow = process.env.KRONO_SMOKE_SCOPE === 'runtime-workspaces' ? '0' : '1'
  const child = spawn('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('scripts/native-smoke.ps1'), '-Executable', exe, '-Fixture', root, '-ReportDirectory', home], { env: { ...process.env, KRONO_USER_DATA: home, KRONO_SMOKE_CLOSE_WINDOW: closeWindow, OPENAI_BASE_URL: root + '/v1' }, stdio: 'inherit', windowsHide: true })
  child.on('exit', code => { server.close(); modelServer?.close(); console.log('Native smoke artifacts:', home); process.exitCode = code ?? 1 })
})
}
if (modelServer) modelServer.listen(1337, '127.0.0.1', startSmoke)
else startSmoke()
