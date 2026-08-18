import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/**
 * The API host runs on 5180 (see src/YuruChara.Api/Properties/launchSettings.json).
 *
 * Requests to /api are proxied there rather than the API enabling CORS. The
 * browser sees one origin, so no preflight happens and no cross-origin policy has
 * to be configured on a server that otherwise serves only public GETs. It also
 * means the frontend code holds no base URL: it fetches "/api/..." in development
 * and in production alike, where the two are served from the same origin anyway.
 */
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5180',
        changeOrigin: true
      }
    }
  }
});
