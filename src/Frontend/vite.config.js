import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [sveltekit()],
  server: {
    host: true, // Allow external connections
    port: 3000,
    // Only proxy in development when connecting to localhost
    ...(process.env.VITE_API_BASE_URL?.includes('localhost') && {
      proxy: {
        '/api': {
          target: process.env.VITE_API_BASE_URL?.replace('/api', '') || 'http://localhost:5000',
          changeOrigin: true,
          secure: false
        }
      }
    })
  },
  preview: {
    host: true,
    port: 3000
  }
});
