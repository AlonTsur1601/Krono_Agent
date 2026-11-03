import { useEffect, useState } from 'react'
import { useSettingsStore } from '@/stores/settingsStore'
import { useThemeStore } from '@/stores/themeStore'

export function SettingsView() {
  const { theme, setTheme } = useThemeStore()
  const {
    providers,
    models,
    selectedProvider,
    selectedModel,
    loadProviders,
    loadModels,
    configureProvider,
    selectProvider,
    selectModel,
  } = useSettingsStore()

  const [apiKey, setApiKey] = useState('')
  const [isConfiguring, setIsConfiguring] = useState(false)

  useEffect(() => {
    loadProviders()
  }, [loadProviders])

  const handleConfigureProvider = async (providerId: string) => {
    if (!apiKey.trim()) return

    setIsConfiguring(true)
    try {
      await configureProvider(providerId, apiKey)
      setApiKey('')
      alert('Provider configured successfully')
    } catch (error) {
      alert(`Failed to configure provider: ${error}`)
    } finally {
      setIsConfiguring(false)
    }
  }

  return (
    <div style={{
      height: '100%',
      overflow: 'auto',
      padding: '2rem',
    }}>
      <div style={{ maxWidth: '800px', margin: '0 auto' }}>
        {/* Theme Settings */}
        <section style={{ marginBottom: '3rem' }}>
          <h2 style={{
            fontSize: '1.5rem',
            fontWeight: 600,
            marginBottom: '1rem',
          }}>
            Appearance
          </h2>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
            <label style={{ fontSize: '0.875rem', color: 'var(--text-muted)' }}>
              Theme
            </label>
            <select
              value={theme}
              onChange={(e) => setTheme(e.target.value as any)}
              style={{
                padding: '0.75rem',
                borderRadius: '0.5rem',
                border: '1px solid var(--border)',
                backgroundColor: 'var(--bg-surface)',
                color: 'var(--text-primary)',
                fontSize: '0.875rem',
              }}
            >
              <option value="dark">Dark</option>
              <option value="light">Light</option>
              <option value="halloween">Halloween</option>
              <option value="system">Follow System</option>
            </select>
          </div>
        </section>

        {/* Model Providers */}
        <section style={{ marginBottom: '3rem' }}>
          <h2 style={{
            fontSize: '1.5rem',
            fontWeight: 600,
            marginBottom: '1rem',
          }}>
            Model Providers
          </h2>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
            {providers.map((provider) => (
              <div
                key={provider.id}
                style={{
                  padding: '1.5rem',
                  borderRadius: '0.75rem',
                  border: '1px solid var(--border)',
                  backgroundColor: 'var(--bg-surface)',
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
                  <h3 style={{ fontSize: '1.125rem', fontWeight: 600 }}>
                    {provider.name}
                  </h3>
                  {provider.configured && (
                    <span style={{
                      padding: '0.25rem 0.75rem',
                      borderRadius: '0.25rem',
                      backgroundColor: 'var(--accent)',
                      color: 'var(--bg-primary)',
                      fontSize: '0.75rem',
                      fontWeight: 600,
                    }}>
                      Configured
                    </span>
                  )}
                </div>

                {!provider.configured && (
                  <div style={{ display: 'flex', gap: '0.5rem' }}>
                    <input
                      type="password"
                      placeholder="API Key"
                      value={apiKey}
                      onChange={(e) => setApiKey(e.target.value)}
                      style={{
                        flex: 1,
                        padding: '0.75rem',
                        borderRadius: '0.5rem',
                        border: '1px solid var(--border)',
                        backgroundColor: 'var(--bg-primary)',
                        color: 'var(--text-primary)',
                      }}
                    />
                    <button
                      onClick={() => handleConfigureProvider(provider.id)}
                      disabled={!apiKey.trim() || isConfiguring}
                      style={{
                        padding: '0 1.5rem',
                        borderRadius: '0.5rem',
                        border: 'none',
                        backgroundColor: 'var(--accent)',
                        color: 'var(--bg-primary)',
                        fontWeight: 600,
                        cursor: apiKey.trim() && !isConfiguring ? 'pointer' : 'not-allowed',
                        opacity: apiKey.trim() && !isConfiguring ? 1 : 0.5,
                      }}
                    >
                      {isConfiguring ? 'Configuring...' : 'Configure'}
                    </button>
                  </div>
                )}

                {provider.configured && (
                  <button
                    onClick={() => {
                      selectProvider(provider.id)
                      loadModels()
                    }}
                    style={{
                      padding: '0.5rem 1rem',
                      borderRadius: '0.5rem',
                      border: '1px solid var(--border)',
                      backgroundColor: selectedProvider === provider.id ? 'var(--accent)' : 'transparent',
                      color: selectedProvider === provider.id ? 'var(--bg-primary)' : 'var(--text-primary)',
                      fontWeight: 600,
                      cursor: 'pointer',
                    }}
                  >
                    {selectedProvider === provider.id ? 'Selected' : 'Select'}
                  </button>
                )}
              </div>
            ))}
          </div>
        </section>

        {/* Available Models */}
        {selectedProvider && models.length > 0 && (
          <section>
            <h2 style={{
              fontSize: '1.5rem',
              fontWeight: 600,
              marginBottom: '1rem',
            }}>
              Available Models
            </h2>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
              {models.map((model) => (
                <div
                  key={model.id}
                  onClick={() => selectModel(model.id)}
                  style={{
                    padding: '1rem',
                    borderRadius: '0.5rem',
                    border: '1px solid var(--border)',
                    backgroundColor: selectedModel === model.id ? 'var(--accent)' : 'var(--bg-surface)',
                    color: selectedModel === model.id ? 'var(--bg-primary)' : 'var(--text-primary)',
                    cursor: 'pointer',
                    transition: 'all 0.15s',
                  }}
                >
                  <div style={{ fontWeight: 600, marginBottom: '0.25rem' }}>
                    {model.name}
                  </div>
                  <div style={{
                    fontSize: '0.75rem',
                    opacity: 0.7,
                  }}>
                    {model.id}
                  </div>
                </div>
              ))}
            </div>
          </section>
        )}
      </div>
    </div>
  )
}
