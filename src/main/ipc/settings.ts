import { z } from 'zod'
import { saveCredential, loadCredential } from '../credentials'
import { ipcMain } from 'electron'
import { agentRuntime } from '../../agent/runtime'
import { AnthropicProvider } from '../../agent/providers/anthropic'
import { GoogleProvider } from '../../agent/providers/google'

// Track configured providers
const configuredProviders = new Set<string>()

export function setupSettingsHandlers() {
  // Register additional providers
  agentRuntime.registerProvider(new AnthropicProvider())
  agentRuntime.registerProvider(new GoogleProvider())

  ipcMain.handle('settings:get-providers', async () => {
    return [
      {
        id: 'openai',
        name: 'OpenAI',
        configured: !!loadCredential('openai'),
      },
      {
        id: 'anthropic',
        name: 'Anthropic',
        configured: !!loadCredential('anthropic'),
      },
      {
        id: 'google',
        name: 'Google (Gemini)',
        configured: !!loadCredential('google'),
      },
    ]
  })

  ipcMain.handle('settings:configure-provider', async (_event, data: { providerId: string; apiKey: string }) => {
    try {
      const { providerId, apiKey } = z.object({ providerId: z.enum(['openai', 'anthropic', 'google']), apiKey: z.string().trim().min(1) }).parse(data)

      // Configure provider in agent runtime
      await agentRuntime.configureProvider(providerId, apiKey)

      // Mark as configured
      configuredProviders.add(providerId)

      saveCredential(providerId, apiKey)

      return { success: true }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to configure provider')
    }
  })

  ipcMain.handle('settings:get-models', async (_event, providerId: string) => {
    try {
      const provider = agentRuntime.getProvider(providerId)
      const credential = loadCredential(providerId)
      if (credential) await agentRuntime.configureProvider(providerId, credential)
      if (!provider) {
        throw new Error(`Provider ${providerId} not found`)
      }

      const models = await provider.listModels()
      return models
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to get models')
    }
  })
}
