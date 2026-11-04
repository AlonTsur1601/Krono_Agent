// Real WinUI window + UI Automation, SQLite, DPAPI, PTY and WebView2.
// Auth/provider endpoints are local fixtures. No owner account is touched.
const http = require('http')
const fs = require('fs')
const os = require('os')
const path = require('path')
const { spawn } = require('child_process')
const home = fs.mkdtempSync(path.join(os.tmpdir(), 'krono-native-'))
const user = { id: '33333333-3333-4333-8333-333333333333', email: 'native-fixture@example.com' }
const requests = []
let revoked = false
let browserIsolated = false
let localNoTools = false
let localKeyIsolated = false
const server = http.createServer((request, response) => {
  if (request.url === '/local/v1/models') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ data: [{ id: 'local-fixture-model' }] })) }
  else if (request.url === '/local/v1/chat/completions') {
    let body = ''; request.on('data', chunk => body += chunk); request.on('end', () => {
      const input = JSON.parse(body)
      localNoTools = !input.tools && input.model === 'local-fixture-model' && input.messages[0].content.includes('Tool calling is disabled')
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
      if (input.tools?.length && !input.input.some(i => i.type === 'function_call_output')) {
        response.write('data: ' + JSON.stringify({ type: 'response.output_item.done', item: { type: 'function_call', call_id: 'native-file-call', name: 'write_file', arguments: JSON.stringify({ path: path.join(home, 'native-tool-proof.txt'), content: 'native approved write' }) } }) + '\n\n')
      } else response.write('data: ' + JSON.stringify({ type: 'response.output_text.delta', delta: 'Native fixture response.' }) + '\n\n')
      response.end('data: ' + JSON.stringify({ type: 'response.completed', response: { usage: { input_tokens: 1, output_tokens: 1 } } }) + '\n\n')
    })
  } else if (request.url === '/assertions') {
    const firstContext = requests[0]?.input[0]?.content || ''
    const secondContext = requests[1]?.input[0]?.content || ''
    response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ browserIsolated, localNoTools, localKeyIsolated, completions: requests.length, chatHasNoTools: requests.length > 0 && !requests[0].tools, globalOnly: firstContext.includes('Native global memory fixture') && !firstContext.includes('Native project memory fixture'), projectScope: secondContext.includes('Native global memory fixture') && secondContext.includes('Native project memory fixture'), toolWrite: fs.existsSync(path.join(home, 'native-tool-proof.txt')) }))
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
