// vite.config.js
import { sveltekit } from "file:///home/machadoleo/repos/conductor_dev/src/Frontend/node_modules/@sveltejs/kit/src/exports/vite/index.js";
import { defineConfig } from "file:///home/machadoleo/repos/conductor_dev/src/Frontend/node_modules/vite/dist/node/index.js";
var vite_config_default = defineConfig({
  plugins: [sveltekit()],
  server: {
    host: true,
    port: 3e3,
    proxy: {
      "/api": {
        target: `http://${process.env.API_HOST}`,
        changeOrigin: true,
        secure: false,
        // CRITICAL: Configure the proxy to forward all headers including Authorization
        configure: (proxy, options) => {
          proxy.on("proxyReq", (proxyReq, req, res) => {
            if (req.headers.authorization) {
              proxyReq.setHeader("Authorization", req.headers.authorization);
            }
            if (req.headers["content-type"]) {
              proxyReq.setHeader("Content-Type", req.headers["content-type"]);
            }
            if (req.headers["user-agent"]) {
              proxyReq.setHeader("User-Agent", req.headers["user-agent"]);
            }
          });
        }
      }
    }
  },
  preview: {
    host: true,
    port: 3e3,
    proxy: {
      "/api": {
        target: `http://${process.env.API_HOST}`,
        changeOrigin: true,
        secure: false,
        // Same configuration for preview mode
        configure: (proxy, options) => {
          proxy.on("proxyReq", (proxyReq, req, res) => {
            if (req.headers.authorization) {
              proxyReq.setHeader("Authorization", req.headers.authorization);
            }
            if (req.headers["content-type"]) {
              proxyReq.setHeader("Content-Type", req.headers["content-type"]);
            }
          });
        }
      }
    }
  }
});
export {
  vite_config_default as default
};
//# sourceMappingURL=data:application/json;base64,ewogICJ2ZXJzaW9uIjogMywKICAic291cmNlcyI6IFsidml0ZS5jb25maWcuanMiXSwKICAic291cmNlc0NvbnRlbnQiOiBbImNvbnN0IF9fdml0ZV9pbmplY3RlZF9vcmlnaW5hbF9kaXJuYW1lID0gXCIvaG9tZS9tYWNoYWRvbGVvL3JlcG9zL2NvbmR1Y3Rvcl9kZXYvc3JjL0Zyb250ZW5kXCI7Y29uc3QgX192aXRlX2luamVjdGVkX29yaWdpbmFsX2ZpbGVuYW1lID0gXCIvaG9tZS9tYWNoYWRvbGVvL3JlcG9zL2NvbmR1Y3Rvcl9kZXYvc3JjL0Zyb250ZW5kL3ZpdGUuY29uZmlnLmpzXCI7Y29uc3QgX192aXRlX2luamVjdGVkX29yaWdpbmFsX2ltcG9ydF9tZXRhX3VybCA9IFwiZmlsZTovLy9ob21lL21hY2hhZG9sZW8vcmVwb3MvY29uZHVjdG9yX2Rldi9zcmMvRnJvbnRlbmQvdml0ZS5jb25maWcuanNcIjtpbXBvcnQgeyBzdmVsdGVraXQgfSBmcm9tICdAc3ZlbHRlanMva2l0L3ZpdGUnO1xuaW1wb3J0IHsgZGVmaW5lQ29uZmlnIH0gZnJvbSAndml0ZSc7XG5cbmV4cG9ydCBkZWZhdWx0IGRlZmluZUNvbmZpZyh7XG4gIHBsdWdpbnM6IFtzdmVsdGVraXQoKV0sXG4gIHNlcnZlcjoge1xuICAgIGhvc3Q6IHRydWUsXG4gICAgcG9ydDogMzAwMCxcbiAgICBwcm94eToge1xuICAgICAgJy9hcGknOiB7XG4gICAgICAgIHRhcmdldDogYGh0dHA6Ly8ke3Byb2Nlc3MuZW52LkFQSV9IT1NUfWAsXG4gICAgICAgIGNoYW5nZU9yaWdpbjogdHJ1ZSxcbiAgICAgICAgc2VjdXJlOiBmYWxzZSxcbiAgICAgICAgLy8gQ1JJVElDQUw6IENvbmZpZ3VyZSB0aGUgcHJveHkgdG8gZm9yd2FyZCBhbGwgaGVhZGVycyBpbmNsdWRpbmcgQXV0aG9yaXphdGlvblxuICAgICAgICBjb25maWd1cmU6IChwcm94eSwgb3B0aW9ucykgPT4ge1xuICAgICAgICAgIHByb3h5Lm9uKCdwcm94eVJlcScsIChwcm94eVJlcSwgcmVxLCByZXMpID0+IHtcbiAgICAgICAgICAgIC8vIEVuc3VyZSBBdXRob3JpemF0aW9uIGhlYWRlciBpcyBmb3J3YXJkZWRcbiAgICAgICAgICAgIGlmIChyZXEuaGVhZGVycy5hdXRob3JpemF0aW9uKSB7XG4gICAgICAgICAgICAgIHByb3h5UmVxLnNldEhlYWRlcignQXV0aG9yaXphdGlvbicsIHJlcS5oZWFkZXJzLmF1dGhvcml6YXRpb24pO1xuICAgICAgICAgICAgfVxuXG4gICAgICAgICAgICAvLyBGb3J3YXJkIG90aGVyIGltcG9ydGFudCBoZWFkZXJzXG4gICAgICAgICAgICBpZiAocmVxLmhlYWRlcnNbJ2NvbnRlbnQtdHlwZSddKSB7XG4gICAgICAgICAgICAgIHByb3h5UmVxLnNldEhlYWRlcignQ29udGVudC1UeXBlJywgcmVxLmhlYWRlcnNbJ2NvbnRlbnQtdHlwZSddKTtcbiAgICAgICAgICAgIH1cblxuICAgICAgICAgICAgaWYgKHJlcS5oZWFkZXJzWyd1c2VyLWFnZW50J10pIHtcbiAgICAgICAgICAgICAgcHJveHlSZXEuc2V0SGVhZGVyKCdVc2VyLUFnZW50JywgcmVxLmhlYWRlcnNbJ3VzZXItYWdlbnQnXSk7XG4gICAgICAgICAgICB9XG4gICAgICAgICAgfSk7XG4gICAgICAgIH1cbiAgICAgIH1cbiAgICB9XG4gIH0sXG4gIHByZXZpZXc6IHtcbiAgICBob3N0OiB0cnVlLFxuICAgIHBvcnQ6IDMwMDAsXG4gICAgcHJveHk6IHtcbiAgICAgICcvYXBpJzoge1xuICAgICAgICB0YXJnZXQ6IGBodHRwOi8vJHtwcm9jZXNzLmVudi5BUElfSE9TVH1gLFxuICAgICAgICBjaGFuZ2VPcmlnaW46IHRydWUsXG4gICAgICAgIHNlY3VyZTogZmFsc2UsXG4gICAgICAgIC8vIFNhbWUgY29uZmlndXJhdGlvbiBmb3IgcHJldmlldyBtb2RlXG4gICAgICAgIGNvbmZpZ3VyZTogKHByb3h5LCBvcHRpb25zKSA9PiB7XG4gICAgICAgICAgcHJveHkub24oJ3Byb3h5UmVxJywgKHByb3h5UmVxLCByZXEsIHJlcykgPT4ge1xuICAgICAgICAgICAgaWYgKHJlcS5oZWFkZXJzLmF1dGhvcml6YXRpb24pIHtcbiAgICAgICAgICAgICAgcHJveHlSZXEuc2V0SGVhZGVyKCdBdXRob3JpemF0aW9uJywgcmVxLmhlYWRlcnMuYXV0aG9yaXphdGlvbik7XG4gICAgICAgICAgICB9XG5cbiAgICAgICAgICAgIGlmIChyZXEuaGVhZGVyc1snY29udGVudC10eXBlJ10pIHtcbiAgICAgICAgICAgICAgcHJveHlSZXEuc2V0SGVhZGVyKCdDb250ZW50LVR5cGUnLCByZXEuaGVhZGVyc1snY29udGVudC10eXBlJ10pO1xuICAgICAgICAgICAgfVxuICAgICAgICAgIH0pO1xuICAgICAgICB9XG4gICAgICB9XG4gICAgfVxuICB9XG59KTsiXSwKICAibWFwcGluZ3MiOiAiO0FBQXFVLFNBQVMsaUJBQWlCO0FBQy9WLFNBQVMsb0JBQW9CO0FBRTdCLElBQU8sc0JBQVEsYUFBYTtBQUFBLEVBQzFCLFNBQVMsQ0FBQyxVQUFVLENBQUM7QUFBQSxFQUNyQixRQUFRO0FBQUEsSUFDTixNQUFNO0FBQUEsSUFDTixNQUFNO0FBQUEsSUFDTixPQUFPO0FBQUEsTUFDTCxRQUFRO0FBQUEsUUFDTixRQUFRLFVBQVUsUUFBUSxJQUFJLFFBQVE7QUFBQSxRQUN0QyxjQUFjO0FBQUEsUUFDZCxRQUFRO0FBQUE7QUFBQSxRQUVSLFdBQVcsQ0FBQyxPQUFPLFlBQVk7QUFDN0IsZ0JBQU0sR0FBRyxZQUFZLENBQUMsVUFBVSxLQUFLLFFBQVE7QUFFM0MsZ0JBQUksSUFBSSxRQUFRLGVBQWU7QUFDN0IsdUJBQVMsVUFBVSxpQkFBaUIsSUFBSSxRQUFRLGFBQWE7QUFBQSxZQUMvRDtBQUdBLGdCQUFJLElBQUksUUFBUSxjQUFjLEdBQUc7QUFDL0IsdUJBQVMsVUFBVSxnQkFBZ0IsSUFBSSxRQUFRLGNBQWMsQ0FBQztBQUFBLFlBQ2hFO0FBRUEsZ0JBQUksSUFBSSxRQUFRLFlBQVksR0FBRztBQUM3Qix1QkFBUyxVQUFVLGNBQWMsSUFBSSxRQUFRLFlBQVksQ0FBQztBQUFBLFlBQzVEO0FBQUEsVUFDRixDQUFDO0FBQUEsUUFDSDtBQUFBLE1BQ0Y7QUFBQSxJQUNGO0FBQUEsRUFDRjtBQUFBLEVBQ0EsU0FBUztBQUFBLElBQ1AsTUFBTTtBQUFBLElBQ04sTUFBTTtBQUFBLElBQ04sT0FBTztBQUFBLE1BQ0wsUUFBUTtBQUFBLFFBQ04sUUFBUSxVQUFVLFFBQVEsSUFBSSxRQUFRO0FBQUEsUUFDdEMsY0FBYztBQUFBLFFBQ2QsUUFBUTtBQUFBO0FBQUEsUUFFUixXQUFXLENBQUMsT0FBTyxZQUFZO0FBQzdCLGdCQUFNLEdBQUcsWUFBWSxDQUFDLFVBQVUsS0FBSyxRQUFRO0FBQzNDLGdCQUFJLElBQUksUUFBUSxlQUFlO0FBQzdCLHVCQUFTLFVBQVUsaUJBQWlCLElBQUksUUFBUSxhQUFhO0FBQUEsWUFDL0Q7QUFFQSxnQkFBSSxJQUFJLFFBQVEsY0FBYyxHQUFHO0FBQy9CLHVCQUFTLFVBQVUsZ0JBQWdCLElBQUksUUFBUSxjQUFjLENBQUM7QUFBQSxZQUNoRTtBQUFBLFVBQ0YsQ0FBQztBQUFBLFFBQ0g7QUFBQSxNQUNGO0FBQUEsSUFDRjtBQUFBLEVBQ0Y7QUFDRixDQUFDOyIsCiAgIm5hbWVzIjogW10KfQo=
