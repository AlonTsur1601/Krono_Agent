import { describe, it, expect } from 'vitest'
import { parseAccountConfig } from './account-config'

describe('desktop account-service configuration', () => {
  it('accepts public configuration and local development', () => {
    expect(parseAccountConfig({ url: 'https://example.supabase.co', anonKey: 'sb_publishable_test' }).url).toContain('https:')
    expect(parseAccountConfig({ url: 'http://127.0.0.1:54321', anonKey: 'public-test-placeholder' }).url).toContain('127.0.0.1')
  })
  it('rejects secret keys and remote insecure endpoints without echoing values', () => {
    for (const input of [
      { url: 'http://remote.example', anonKey: 'public-test-placeholder' },
      { url: 'https://example.supabase.co', anonKey: 'sb_secret_test-do-not-echo' },
      { url: 'https://user:password@example.com', anonKey: 'public-test-placeholder' },
    ]) expect(() => parseAccountConfig(input)).toThrow()
    const jwt = `header.${Buffer.from(JSON.stringify({ role: 'service_role' })).toString('base64url')}.signature`
    expect(() => parseAccountConfig({ url: 'https://example.supabase.co', anonKey: jwt })).toThrow('never a secret/service-role')
  })
})
