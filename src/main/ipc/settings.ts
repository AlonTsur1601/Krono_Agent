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
        configured: process.env.OPENAI_API_KEY !== undefined || configuredProviders.has('openai'),
      },
      {
        id: 'anthropic',
        name: 'Anthropic',
        configured: process.env.ANTHROPIC_API_KEY !== undefined || configuredProviders.has('anthropic'),
      },
      {
        id: 'google',
        name: 'Google (Gemini)',
        configured: process.env.GOOGLE_API_KEY !== undefined || configuredProviders.has('google'),
      },
    ]
  })

  ipcMain.handle('settings:configure-provider', async (_event, data: { providerId: string; apiKey: string }) => {
    try {
      const { providerId, apiKey } = data

      // Configure provider in agent runtime
      await agentRuntime.configureProvider(providerId, apiKey)

      // Mark as configured
      configuredProviders.add(providerId)

      // TODO: Store API key securely in OS keychain instead of environment variable
      process.env[`${providerId.toUpperCase()}_API_KEY`] = apiKey

      return { success: true }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to configure provider')
    }
  })

  ipcMain.handle('settings:get-models', async (_event, providerId: string) => {
    try {
      const provider = (agentRuntime as any).providers.get(providerId)
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
