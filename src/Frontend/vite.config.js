import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [sveltekit()],
  server: {
    host: true, 
    port: 3000,
    proxy: {
      '/api': {
        target: `http://conductor-api:${process.env.PORT_NUMBER}`,
        changeOrigin: true,
        secure: false
      }
    }
  },
  preview: {
    host: true,
    port: 3000,
    proxy: {
      '/api': {
        target: `http://conductor-api:${process.env.PORT_NUMBER}`,
        changeOrigin: true,
        secure: false
      }
    }
  }
});