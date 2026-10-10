import '@fontsource/lexend/400.css'
import '@fontsource/lexend/500.css'
import '@fontsource/lexend/600.css'
import '@fontsource/quicksand/600.css'
import '@fontsource/quicksand/700.css'
import './index.css'

import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { euQuery } from './features/acesso/acesso'
import { ErroDaApi } from './lib/api'
import { rotas } from './rotas'

// Fontes empacotadas no app (não no Google Fonts): funcionam offline no PWA.
const clienteDeConsultas: QueryClient = new QueryClient({
  // Sessão caiu no meio do uso (revogada, expirou): esquece o usuário e as guardas mandam para o login.
  queryCache: new QueryCache({
    onError: (erro) => {
      if (erro instanceof ErroDaApi && erro.status === 401) clienteDeConsultas.setQueryData(euQuery.queryKey, null)
    },
  }),
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: true,
      // Não insiste em erro de acesso (401/403) nem em 404: a resposta não vai mudar.
      retry: (tentativas, erro) => !(erro instanceof ErroDaApi && [401, 403, 404].includes(erro.status)) && tentativas < 1,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={clienteDeConsultas}>
      <RouterProvider router={rotas} />
    </QueryClientProvider>
  </StrictMode>,
)
