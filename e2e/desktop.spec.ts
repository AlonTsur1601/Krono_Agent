import { test, expect, _electron as electron } from '@playwright/test'
import http from 'http'
import { mkdtempSync } from 'fs'
import { tmpdir } from 'os'
import path from 'path'

test('desktop terminal and isolated Chromium with local auth fixture', async () => {
  // This fixture is NOT live Supabase acceptance. PTY, SQLite and browser are real.
  const user = { id: '11111111-1111-4111-8111-111111111111', email: 'fixture@example.com', aud: 'authenticated', role: 'authenticated', created_at: new Date().toISOString() }
  const server = http.createServer((req, res) => {
    if (req.url?.startsWith('/auth/')) { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify({ access_token: 'test-access-token', refresh_token: 'test-refresh-token', expires_in: 3600, token_type: 'bearer', user })) }
    else if (req.url === '/download') { res.setHeader('Content-Disposition', 'attachment; filename="krono-fixture.txt"'); res.end('Krono download fixture') }
    else { res.setHeader('Content-Type', 'text/html'); res.end(`<title>${req.url === '/private' ? 'Private probe' : 'Browser probe'}</title><h1>Embedded Chromium works</h1>`) }
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  const root = `http://127.0.0.1:${(server.address() as { port: number }).port}`
  const env = { ...process.env, SUPABASE_URL: root, SUPABASE_ANON_KEY: 'test-public-placeholder' }
  delete env.ELECTRON_RUN_AS_NODE
  const app = await electron.launch({ args: ['.'], env })
  try {
    await app.evaluate(({ app }, dir) => app.setPath('userData', dir), mkdtempSync(path.join(tmpdir(), 'krono-desktop-e2e-')))
    const page = await app.firstWindow()
    const errors: string[] = []; page.on('pageerror', error => errors.push(error.message))
    await page.locator('input[type=email]').fill(user.email)
    await page.locator('input[type=password]').fill('fixture-password')
    await page.locator('button[type=submit]').click()
    await expect(page.getByRole('button', { name: 'Sign Out' })).toBeVisible()
    await page.evaluate(() => {
      const state = window as unknown as { ptyOutput: string; ptyId: string }
      state.ptyOutput = ''
      window.api.on('terminal:data', event => { state.ptyId = event.id; state.ptyOutput += event.data })
    })
    await page.getByRole('button', { name: 'Terminal +', exact: true }).click()
    await page.waitForFunction(() => !!(window as unknown as { ptyId: string }).ptyId)
    await page.evaluate(command => window.api.invoke('terminal:write', { id: (window as unknown as { ptyId: string }).ptyId, data: command }), process.platform === 'win32' ? 'Write-Output (6*7)\r' : 'echo $((6*7))\r')
    await page.waitForFunction(() => (window as unknown as { ptyOutput: string }).ptyOutput.includes('42'))
    await page.getByRole('button', { name: 'Browser', exact: true }).click()
    await page.getByLabel('Address or search').fill(root + '/probe')
    await page.getByRole('button', { name: 'Go', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Browser probe', exact: true })).toBeVisible()
    const isolation = await app.evaluate(async ({ webContents }, url) => {
      const content = webContents.getAllWebContents().find(content => content.getURL() === url)!
      return { ipc: await content.executeJavaScript('typeof window.api'), node: await content.executeJavaScript('typeof require') }
    }, root + '/probe')
    expect(isolation).toEqual({ ipc: 'undefined', node: 'undefined' })
    await page.getByRole('button', { name: 'Bookmark', exact: true }).click()
    const library = await page.evaluate(() => window.api.invoke('browser:action', { action: 'library' }))
    expect(library.bookmarks.some((entry: { url: string }) => entry.url === root + '/probe')).toBe(true)
    expect(library.history.some((entry: { url: string }) => entry.url === root + '/probe')).toBe(true)
    await page.getByRole('button', { name: 'Private tab', exact: true }).click()
    await page.getByLabel('Address or search').fill(root + '/private')
    await page.getByRole('button', { name: 'Go', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Private · Private probe', exact: true })).toBeVisible()
    const afterPrivate = await page.evaluate(() => window.api.invoke('browser:action', { action: 'library' }))
    expect(afterPrivate.history.some((entry: { url: string }) => entry.url === root + '/private')).toBe(false)
    await page.getByRole('button', { name: 'Close Private probe', exact: true }).click()
    const downloadDir = mkdtempSync(path.join(tmpdir(), 'krono-download-e2e-'))
    await app.evaluate(({ session }, file) => session.fromPartition('persist:browser-11111111-1111-4111-8111-111111111111').once('will-download', (_event, item) => item.setSavePath(file)), path.join(downloadDir, 'krono-fixture.txt'))
    await page.getByLabel('Address or search').fill(root + '/download')
    await page.getByRole('button', { name: 'Go', exact: true }).click()
    await expect(page.getByText(/krono-fixture.txt · completed/)).toBeVisible()
    await page.getByRole('button', { name: 'New tab', exact: true }).click()
    await page.getByRole('button', { name: 'Browser probe', exact: true }).click()
    await page.getByLabel('Find in page').fill('Chromium')
    await page.getByRole('button', { name: 'Chat', exact: true }).click()
    await expect(page.getByText('Select a chat or create a new one')).toBeVisible()
    await page.getByRole('button', { name: 'Projects & memory', exact: true }).click()
    await page.getByLabel('Project name', { exact: true }).fill('Fixture project')
    await page.getByLabel('Project instructions', { exact: true }).fill('Use focused verification.')
    await page.getByLabel('Project memory', { exact: true }).fill('Project-only accepted memory')
    await page.getByRole('button', { name: 'Save project', exact: true }).click()
    await expect(page.getByRole('button', { name: 'New chat in Fixture project', exact: true })).toBeVisible()
    await page.getByLabel('Global memory', { exact: true }).fill('Global accepted preference')
    await page.getByRole('button', { name: 'Save global memory', exact: true }).click()
    const projectFolder = mkdtempSync(path.join(tmpdir(), 'krono-project-e2e-'))
    const project = await page.evaluate(async directory => {
      const projects = await window.api.invoke('projects:list')
      const project = projects.find((project: { name: string }) => project.name === 'Fixture project')
      await window.api.invoke('projects:save', { id: project.id, name: project.name, directories: [directory], instructions: project.instructions, memory: project.memory, permissions: 'project' })
      return (await window.api.invoke('projects:list')).find((item: { id: string }) => item.id === project.id)
    }, projectFolder)
    expect(project.directories).toHaveLength(1)
    expect(project.memories).toContain('Global accepted preference')
    expect(project.memories).toContain('Project-only accepted memory')
    await page.getByRole('button', { name: 'New chat in Fixture project', exact: true }).click()
    await expect(page.getByText('Start a conversation', { exact: true })).toBeVisible()
    const chat = await page.evaluate(async () => (await window.api.invoke('chat:list'))[0])
    expect(chat.projectId).toBe(project.id)
    await expect(page.getByLabel('Permissions', { exact: true })).toHaveValue('project')
    const otherProject = await page.evaluate(() => window.api.invoke('projects:save', { name: 'Other project', directories: [], instructions: '', memory: 'Other project private memory', permissions: 'ask' }))
    const contexts = await page.evaluate(() => window.api.invoke('projects:list'))
    expect(contexts.find((item: { id: string }) => item.id === project.id).memories).not.toContain('Other project private memory')
    expect(contexts.find((item: { id: string }) => item.id === otherProject.id).memories).not.toContain('Project-only accepted memory')
    await page.getByRole('button', { name: 'Projects & memory', exact: true }).click()
    await page.getByLabel('Project for New Chat', { exact: true }).selectOption(otherProject.id)
    await expect(page.getByLabel('Project for New Chat', { exact: true })).toHaveValue(otherProject.id)
    const moved = await page.evaluate(async () => (await window.api.invoke('chat:list'))[0])
    expect(moved.projectId).toBe(otherProject.id)
    await page.screenshot({ path: path.join(tmpdir(), 'krono-desktop-e2e.png') })
    await page.getByRole('button', { name: 'Sign Out' }).click()
    await expect(page.locator('input[type=email]')).toBeVisible()
    expect(errors).toEqual([])
  } finally { await app.close(); server.close() }
})
