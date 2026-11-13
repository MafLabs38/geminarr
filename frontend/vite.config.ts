/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';

const src = fileURLToPath(new URL('./src', import.meta.url));

// Absolute imports from src: every top-level folder becomes an alias.
const aliases = [
  '__tests__',
  'App',
  'Components',
  'Helpers',
  'Identity',
  'Login',
  'Overview',
  'Session',
  'Settings',
  'Styles',
  'System',
  'Utilities',
].map((folder) => ({
  find: new RegExp(`^${folder}(/|$)`),
  replacement: `${src}/${folder}$1`,
}));

export default defineConfig({
  root: fileURLToPath(new URL('.', import.meta.url)),
  plugins: [react()],
  resolve: { alias: aliases },
  css: {
    modules: { localsConvention: 'camelCaseOnly' },
  },
  server: {
    port: 5173,
    // GEMINARR_API: where the backend runs (for example a dotnet watch on another port).
    proxy: {
      '/api': process.env.GEMINARR_API ?? 'http://localhost:8282',
      '/openapi': process.env.GEMINARR_API ?? 'http://localhost:8282',
    },
  },
  build: {
    outDir: fileURLToPath(new URL('../_output/UI', import.meta.url)),
    emptyOutDir: true,
    // Fonts stay files: Vite would otherwise inline the small ones as data: URIs, which the Content-Security-Policy refuses.
    assetsInlineLimit: (file) => (/\.woff2?$/.test(file) ? false : undefined),
  },
  test: {
    environment: 'jsdom',
    globals: true,
    // A test that stubs fetch cannot leak the stub into the next one.
    unstubGlobals: true,
    setupFiles: ['./src/setupTests.ts'],
    css: { modules: { classNameStrategy: 'non-scoped' } },
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
      reportsDirectory: '../coverage/frontend',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/__tests__/**', 'src/index.tsx', 'src/setupTests.ts'],
      // The gate of docs/TESTING.md §5: a pull request that lets coverage fall under it fails (higher than Mnemarr's 70 %: the code base is small and starts clean).
      thresholds: { statements: 85, branches: 80, functions: 85, lines: 85 },
    },
  },
});
