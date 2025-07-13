import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [sveltekit()],
  server: {
    host: true,
    port: 3000,
    proxy: {
      '/api': {
        target: `http://${process.env.API_HOST}`,
        changeOrigin: true,
        secure: false,
        // CRITICAL: Configure the proxy to forward all headers including Authorization
        configure: (proxy, options) => {
          proxy.on('proxyReq', (proxyReq, req, res) => {
            // Ensure Authorization header is forwarded
            if (req.headers.authorization) {
              proxyReq.setHeader('Authorization', req.headers.authorization);
            }

            // Forward other important headers
            if (req.headers['content-type']) {
              proxyReq.setHeader('Content-Type', req.headers['content-type']);
            }

            if (req.headers['user-agent']) {
              proxyReq.setHeader('User-Agent', req.headers['user-agent']);
            }
          });
        }
      }
    }
  },
  preview: {
    host: true,
    port: 3000,
    proxy: {
      '/api': {
        target: `http://${process.env.API_HOST}`,
        changeOrigin: true,
        secure: false,
        // Same configuration for preview mode
        configure: (proxy, options) => {
          proxy.on('proxyReq', (proxyReq, req, res) => {
            if (req.headers.authorization) {
              proxyReq.setHeader('Authorization', req.headers.authorization);
            }

            if (req.headers['content-type']) {
              proxyReq.setHeader('Content-Type', req.headers['content-type']);
            }
          });
        }
      }
    }
  }
});