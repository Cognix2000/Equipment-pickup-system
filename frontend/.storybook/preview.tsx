import '@fontsource/lexend/400.css'
import '@fontsource/lexend/500.css'
import '@fontsource/lexend/600.css'
import '@fontsource/quicksand/600.css'
import '@fontsource/quicksand/700.css'
import '../src/index.css'

import type { Preview } from '@storybook/react-vite'
import { MemoryRouter } from 'react-router'

// As três larguras em que toda tela é revisada: celular, tablet e desktop.
const larguras = {
  celular: { name: 'Celular 390', styles: { width: '390px', height: '844px' } },
  tablet: { name: 'Tablet 820', styles: { width: '820px', height: '1180px' } },
  desktop: { name: 'Desktop 1280', styles: { width: '1280px', height: '800px' } },
}

const preview: Preview = {
  parameters: {
    viewport: { options: larguras },
    backgrounds: { options: { fundo: { name: 'Fundo do app', value: '#f5f5f5' } } },
    layout: 'padded',
  },
  initialGlobals: {
    viewport: { value: 'celular', isRotated: false },
    backgrounds: { value: 'fundo' },
  },
  decorators: [
    (Historia) => (
      <MemoryRouter>
        <Historia />
      </MemoryRouter>
    ),
  ],
}

export default preview
