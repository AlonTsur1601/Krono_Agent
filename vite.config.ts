import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src/renderer'),
      '@/components': path.resolve(__dirname, './src/renderer/components'),
      '@/features': path.resolve(__dirname, './src/renderer/features'),
      '@/lib': path.resolve(__dirname, './src/renderer/lib'),
      '@/stores': path.resolve(__dirname, './src/renderer/stores'),
      '@/hooks': path.resolve(__dirname, './src/renderer/hooks'),
      '@/agent': path.resolve(__dirname, './src/agent'),
      '@/games': path.resolve(__dirname, './src/games'),
      '@/shared': path.resolve(__dirname, './src/shared'),
    },
  },
  build: {
    outDir: 'dist/renderer',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
  },
})
