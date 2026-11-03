import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'
import { fileURLToPath } from 'url'
const rootDir = path.dirname(fileURLToPath(import.meta.url))

export default defineConfig({
  base: './',
  root: 'src/renderer',
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(rootDir, './src/renderer'),
      '@/components': path.resolve(rootDir, './src/renderer/components'),
      '@/features': path.resolve(rootDir, './src/renderer/features'),
      '@/lib': path.resolve(rootDir, './src/renderer/lib'),
      '@/stores': path.resolve(rootDir, './src/renderer/stores'),
      '@/hooks': path.resolve(rootDir, './src/renderer/hooks'),
      '@/agent': path.resolve(rootDir, './src/agent'),
      '@/games': path.resolve(rootDir, './src/games'),
      '@/shared': path.resolve(rootDir, './src/shared'),
    },
  },
  build: {
    outDir: '../../dist/renderer',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
  },
})
