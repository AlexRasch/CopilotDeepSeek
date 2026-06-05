import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite'

import path from 'path';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    plugin(),
    tailwindcss()
  ],
    server: {
        port: 40247,
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    }
  }
})
