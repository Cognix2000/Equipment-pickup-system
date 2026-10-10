import type { StorybookConfig } from '@storybook/react-vite'

const config: StorybookConfig = {
  stories: ['../src/**/*.stories.@(ts|tsx)'],
  framework: '@storybook/react-vite',
  // O plugin do PWA não faz sentido dentro do Storybook.
  viteFinal: async (vite) => ({
    ...vite,
    plugins: (vite.plugins ?? []).flat().filter((p) => !(p && typeof p === 'object' && 'name' in p && String(p.name).startsWith('vite-plugin-pwa'))),
  }),
}

export default config
