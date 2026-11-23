import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentRuntime } from '../runtime'
import type { CompletionRequest, ExecutionContext, ProviderAdapter } from '../types'
import { PluginConfig, pluginGuidance, pluginTools, testPlugin } from './plugins'

const firebaseHeaders = vi.hoisted(() => vi.fn())
vi.mock('google-auth-library', () => ({ GoogleAuth: class { getRequestHeaders = firebaseHeaders } }))

const credential = 'fixture-private-plugin-token'
const context: ExecutionContext = {
  permissions: 'full', projectDirs: [],
  requestApproval: async () => ({ approved: false }), emitProgress: () => {},
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetAllMocks()
})

describe('optional plugins', () => {
  it('applies only installed skill guidance and exposes no executable tool for a skill pack', () => {
    const config = PluginConfig.parse({ id: 'typescript', skill: true })
    expect(pluginGuidance([config])).toContain('nearest tsconfig')
    expect(pluginGuidance([config])).not.toContain('For Vue work')
    expect(pluginTools([config])).toEqual([])
    expect(PluginConfig.safeParse({ id: 'unknown-skill', skill: true }).success).toBe(false)
  })

  it('registers only tools listed in an installed package', () => {
    const enabled = PluginConfig.parse({ id: 'npm', tools: ['npm_get_package', 'github_list_repositories'] })
    const disabled = PluginConfig.parse({ id: 'npm', tools: [] })
    expect(pluginTools([enabled]).map(tool => tool.name)).toEqual(['npm_get_package'])
    expect(pluginTools([disabled])).toEqual([])
  })

  it('searches Hugging Face public models without credentials and projects only catalog data', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(Response.json([
      { id: 'example/test-model', pipeline_tag: 'text-generation', downloads: 25, ignored: 'omitted' },
    ]))
    vi.stubGlobal('fetch', fetch)
    const [tool] = pluginTools([PluginConfig.parse({ id: 'huggingface' })])
    const result = await tool.execute({ search: 'test', task: 'text-generation' }, context)
    expect(String(fetch.mock.calls[0][0])).toBe('https://huggingface.co/api/models?search=test&limit=15&pipeline_tag=text-generation')
    expect(fetch.mock.calls[0][1]?.headers).toBeUndefined()
    expect(JSON.parse(result.output!)).toEqual({ models: [{ id: 'example/test-model', pipeline_tag: 'text-generation', downloads: 25 }] })
  })

  it('checks GitHub sign-in and reads repository metadata without exposing a credential', async () => {
    const config = PluginConfig.parse({ id: 'github', token: credential })
    const fetch = vi.fn<typeof globalThis.fetch>().mockImplementation(async () => new Response(JSON.stringify({ login: 'octocat' })))
    vi.stubGlobal('fetch', fetch)
    expect(await testPlugin(config)).toMatchObject({ success: true })
    expect(String(fetch.mock.calls[0][0])).toBe('https://api.github.com/user')

    fetch.mockResolvedValueOnce(new Response(JSON.stringify([{ full_name: 'octocat/hello', description: credential, clone_url: 'hidden' }]), {
      headers: { Link: '<https://api.github.com/user/repos?page=2>; rel="next"' },
    }))
    const result = await pluginTools([config])[0].execute({ page: 1 }, context)
    expect(JSON.parse(result.output!)).toEqual({ repositories: [{ full_name: 'octocat/hello', description: '[redacted]' }], hasMore: true })
    expect(fetch.mock.calls[1][1]).toMatchObject({ method: 'GET', redirect: 'error' })
    expect(new Headers(fetch.mock.calls[1][1]?.headers).get('Authorization')).toBe('Bearer ' + credential)
  })

  it('reads Vercel deployments only for the configured team and returns selected fields', async () => {
    const config = PluginConfig.parse({ id: 'vercel', token: credential, teamId: 'team_fixture' })
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValueOnce(Response.json({ projects: [] })).mockResolvedValueOnce(Response.json({
      deployments: [{ name: 'my-app', uid: 'dpl_123', state: 'READY', env: { hidden: 'private' } }], pagination: { next: 123 },
    }))
    vi.stubGlobal('fetch', fetch)
    expect(await testPlugin(config)).toMatchObject({ success: true })
    const tool = pluginTools([config]).find(tool => tool.name === 'vercel_list_deployments')!
    const result = await tool.execute({ projectId: 'prj_123', teamId: 'team_other' }, context)
    const url = new URL(String(fetch.mock.calls[1][0]))
    expect(url.pathname).toBe('/v7/deployments')
    expect(url.searchParams.get('teamId')).toBe('team_fixture')
    expect(url.searchParams.get('projectId')).toBe('prj_123')
    expect(JSON.parse(result.output!)).toEqual({ deployments: [{ uid: 'dpl_123', name: 'my-app', state: 'READY' }], next: 123 })
  })

  it('uses Firebase Application Default Credentials and removes key IDs from app results', async () => {
    const config = PluginConfig.parse({ id: 'firebase' })
    firebaseHeaders.mockResolvedValue(new Headers({ Authorization: 'Bearer ' + credential, 'X-Goog-User-Project': 'fixture-project' }))
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValueOnce(Response.json({})).mockResolvedValueOnce(Response.json({
      apps: [{ displayName: 'Demo', appId: 'fixture-app', platform: 'WEB', apiKeyId: 'private-key-id' }], nextPageToken: 'p2',
    }))
    vi.stubGlobal('fetch', fetch)
    expect(await testPlugin(config)).toMatchObject({ success: true })
    const result = await pluginTools([config])[1].execute({ projectId: 'fixture-project' }, context)
    expect(new URL(String(fetch.mock.calls[1][0])).pathname).toBe('/v1beta1/projects/fixture-project:searchApps')
    expect(new Headers(fetch.mock.calls[1][1]?.headers).get('X-Goog-User-Project')).toBe('fixture-project')
    expect(JSON.parse(result.output!)).toEqual({ apps: [{ displayName: 'Demo', appId: 'fixture-app', platform: 'WEB' }], nextPageToken: 'p2' })
    firebaseHeaders.mockRejectedValueOnce(new Error(credential))
    expect(await testPlugin(config)).toMatchObject({ success: false, message: expect.stringContaining('gcloud auth application-default login') })
  })

  it('requires per-action approval for cloud reads and stops cloud tools at the Project permission boundary', async () => {
    const runtime = new AgentRuntime()
    const requests: CompletionRequest[] = []
    runtime.registerProvider({
      id: 'fixture',
      getModelCapabilities: () => ({ supportsReasoningEffort: false, supportsFastMode: false }),
      async *createCompletion(request: CompletionRequest) {
        requests.push(structuredClone({ ...request, signal: undefined }))
        if (request.tools?.some(tool => tool.name === 'github_list_repositories') && request.messages.at(-1)?.role !== 'tool') {
          yield { type: 'tool_call', toolCall: { id: 'call1', name: 'github_list_repositories', arguments: {} } }
        } else yield { type: 'done' }
      },
    } as unknown as ProviderAdapter)
    const fetch = vi.fn<typeof globalThis.fetch>().mockImplementation(async () => Response.json([]))
    vi.stubGlobal('fetch', fetch)
    const run = async (permissions: 'ask' | 'project', approve: boolean) => {
      for await (const chunk of runtime.execute([], {
        mode: 'agent', permissions, providerId: 'fixture', modelId: 'demo', requestApproval: async () => ({ approved: approve }),
      })) void chunk
    }
    await run('ask', true)
    expect(requests.at(-1)?.tools?.map(tool => tool.name)).not.toContain('github_list_repositories')
    runtime.registerTools(pluginTools([PluginConfig.parse({ id: 'github', token: credential })]))
    await expect(run('ask', false)).rejects.toThrow(/approval/i)
    await run('project', true)
    expect(requests.at(-1)?.tools?.map(tool => tool.name)).not.toContain('github_list_repositories')
    expect(fetch).not.toHaveBeenCalled()
    await run('ask', true)
    expect(fetch).toHaveBeenCalledTimes(1)
    expect(requests.at(-1)?.messages.at(-1)?.role).toBe('tool')
    expect(JSON.stringify(requests)).not.toContain(credential)
  })

  it('returns a friendly status when a provider replies with an error body', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(Response.json({ message: credential }, { status: 401 }))
    vi.stubGlobal('fetch', fetch)
    expect(await testPlugin(PluginConfig.parse({ id: 'github', token: credential }))).toEqual({
      success: false, message: 'Sign-in expired or the access token is invalid.',
    })
  })

  it('inspects a workspace Node manifest without reading outside Project permissions or executing scripts', async () => {
    const tmp = await mkdtemp(join(tmpdir(), 'krono-plugin-'))
    const project = join(tmp, 'project')
    await mkdir(project)
    try {
      const manifest = JSON.stringify({ name: 'project', scripts: { test: 'private-command' }, engines: { node: '>=24' }, dependencies: { example: 'secret-version' } })
      await writeFile(join(project, 'package.json'), manifest)
      await writeFile(join(tmp, 'package.json'), '{"name":"outside"}')
      const [tool] = pluginTools([PluginConfig.parse({ id: 'node' })])
      const projectContext = { ...context, projectDirs: [project], permissions: 'project' as const }
      const result = await tool.execute({}, projectContext)
      expect(JSON.parse(result.output!)).toMatchObject({ name: 'project', engine: { node: '>=24' }, scripts: ['test'], dependencies: ['example'] })
      expect(result.output).not.toMatch(/private-command|secret-version/)
      expect(await tool.execute({ directory: '..' }, projectContext)).toMatchObject({ success: false })
    } finally { await rm(tmp, { recursive: true, force: true }) }
  })
})
