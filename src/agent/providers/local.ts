import { z } from 'zod'
import { spawn, type ChildProcess } from 'node:child_process'
import { existsSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { CompatibleProvider } from './compatible'
import type { CompletionRequest, Model, ProviderConfig } from '../types'

const modelName = z.string().min(1).max(360)
const ollamaTags = z.object({ models: z.array(z.object({
  name: modelName, remote_host: z.string().optional(), remote_model: z.string().optional(),
})) })
const studioModels = z.object({ data: z.array(z.object({ id: modelName, type: z.string() })) })
const janModels = z.object({ data: z.array(z.object({ id: modelName })) })

const sources = [
  { name: 'Ollama', origin: 'http://127.0.0.1:11434', path: '/api/tags', parse: (data: unknown) =>
    ollamaTags.parse(data).models.filter(model => !model.remote_host && !model.remote_model &&
      !/(?:[:-]cloud)(?:$|:)/i.test(model.name)).map(model => model.name) },
  { name: 'LM Studio', origin: 'http://127.0.0.1:1234', path: '/api/v0/models', parse: (data: unknown) =>
    studioModels.parse(data).data.filter(model => model.type === 'llm' || model.type === 'vlm').map(model => model.id) },
  { name: 'Jan', origin: 'http://127.0.0.1:1337', path: '/v1/models', parse: (data: unknown) =>
    janModels.parse(data).data.map(model => model.id) },
] as const

const label = (id: string, source: string) => `${id} · ${source}`

let ollama: ChildProcess | undefined
let ollamaStartup: Promise<string[]> | undefined

async function readCatalog(source: typeof sources[number], signal?: AbortSignal): Promise<string[] | null> {
  const timeout = AbortSignal.timeout(1800)
  try {
    const response = await fetch(source.origin + source.path, { redirect: 'error', signal: signal ? AbortSignal.any([signal, timeout]) : timeout })
    if (!response.ok) { await response.body?.cancel(); return [] }
    return [...new Set(source.parse(await response.json()))]
  } catch (error) {
    const cause = error instanceof Error ? error.cause as NodeJS.ErrnoException | undefined : undefined
    return cause?.code === 'ECONNREFUSED' ? null : []
  }
}

async function startOllama(source: typeof sources[0]) {
  const { LOCALAPPDATA, USERPROFILE, OLLAMA_MODELS } = process.env
  if (process.platform !== 'win32' || !LOCALAPPDATA || !USERPROFILE) return []
  const executable = join(LOCALAPPDATA, 'Programs', 'Ollama', 'ollama.exe')
  const manifests = join(OLLAMA_MODELS || join(USERPROFILE, '.ollama', 'models'), 'manifests')
  if (!existsSync(executable) || !existsSync(manifests) || !readdirSync(manifests).length) return []
  if (!ollama) {
    const child = spawn(executable, ['serve'], { windowsHide: true, stdio: 'ignore',
      env: { ...process.env, OLLAMA_HOST: '127.0.0.1:11434', OLLAMA_NO_CLOUD: '1' } })
    ollama = child
    const clear = () => { if (ollama === child) ollama = undefined }
    child.once('exit', clear)
    child.once('error', clear)
  }
  for (let attempt = 0; attempt < 16; attempt++) {
    const models = await readCatalog(source)
    if (models !== null) return models
    if (!ollama) break
    await delay(125)
  }
  return []
}

async function discover(source: typeof sources[number], signal?: AbortSignal): Promise<string[]> {
  const models = await readCatalog(source, signal)
  if (models !== null || source.name !== 'Ollama' || signal?.aborted) return models ?? []
  ollamaStartup ??= startOllama(source).catch(() => []).finally(() => { ollamaStartup = undefined })
  return ollamaStartup
}

export function stopLocalRuntime() {
  ollama?.kill()
}

export class LocalProvider extends CompatibleProvider {
  private allowTools = false
  constructor() { super('local') }

  override async configure(config: ProviderConfig) {
    this.allowTools = config.toolsEnabled === true
  }

  override async listModels(): Promise<Model[]> {
    const catalogs = await Promise.all(sources.map(source => discover(source)))
    return catalogs.flatMap((ids, index) => ids.map(id => {
      const name = label(id, sources[index].name)
      return { id: name, name, providerId: this.id, capabilities: this.getModelCapabilities(name) }
    }))
  }

  override async testConnection() {
    const start = Date.now()
    return (await this.listModels()).length > 0
      ? { success: true, latencyMs: Date.now() - start }
      : { success: false, error: 'No local models found. Start Ollama, LM Studio or Jan and make a model available.' }
  }

  override getModelCapabilities(modelId: string) {
    return { ...super.getModelCapabilities(modelId), supportsTools: this.allowTools }
  }

  override async *createCompletion(request: CompletionRequest) {
    const source = sources.find(candidate => request.model.endsWith(' · ' + candidate.name))
    const ids = source ? await discover(source, request.signal) : []
    if (request.signal?.aborted) throw new Error('Run cancelled')
    const model = source && ids.find(id => label(id, source.name) === request.model)
    if (!source || !model) throw new Error('The selected local model is unavailable. Start its server and refresh the model list.')
    const provider = new CompatibleProvider('local')
    await provider.configure({ apiKey: '', endpoint: source.origin + '/v1', toolsEnabled: this.allowTools })
    yield* provider.createCompletion({ ...request, model })
  }
}
