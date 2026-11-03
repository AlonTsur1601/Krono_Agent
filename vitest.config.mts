import { defineConfig } from 'vitest/config'
import path from 'path'
import { fileURLToPath } from 'url'
const rootDir = path.dirname(fileURLToPath(import.meta.url))

export default defineConfig({
  test: {
    include: ['src/**/*.test.ts'],
    globals: true,
    environment: 'node',
  },
  resolve: {
    alias: {
      '@': path.resolve(rootDir, './src/renderer'),
      '@/shared': path.resolve(rootDir, './src/shared'),
    },
  },
})
