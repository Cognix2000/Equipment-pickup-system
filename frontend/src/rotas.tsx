import { createBrowserRouter, Link } from 'react-router'
import { Vazio } from '@/components/EstadoDaTela'
import { Inicio, RotaProtegida } from '@/features/acesso/RotaProtegida'
import { Entrar } from '@/paginas/acesso/Entrar'
import { PrimeiroAcesso } from '@/paginas/acesso/PrimeiroAcesso'
import { LayoutDaConta } from '@/paginas/conta/LayoutDaConta'
import { MinhaConta } from '@/paginas/conta/MinhaConta'
import { DetalheDaOrdem } from '@/paginas/painel/DetalheDaOrdem'
import { Equipe } from '@/paginas/painel/Equipe'
import { Importar } from '@/paginas/painel/Importar'
import { LayoutPainel } from '@/paginas/painel/LayoutPainel'
import { OrdensFinanceiro } from '@/paginas/painel/OrdensFinanceiro'
import { Repasses } from '@/paginas/painel/Repasses'
import { Usuarios } from '@/paginas/painel/Usuarios'
import { Conferir } from '@/paginas/estoque/Conferir'
import { DetalheOrdem } from '@/paginas/tecnico/DetalheOrdem'
import { RegistrarRetirada } from '@/paginas/tecnico/RegistrarRetirada'
import { Resgate } from '@/paginas/tecnico/Resgate'
import { VisitaSemSucesso } from '@/paginas/tecnico/VisitaSemSucesso'
import { Envios } from '@/paginas/tecnico/Envios'
import { LayoutTecnico } from '@/paginas/tecnico/LayoutTecnico'
import { MinhasOrdens } from '@/paginas/tecnico/MinhasOrdens'

// O servidor confere perfil e dono em toda requisição (RNF04); estas guardas só evitam abrir tela proibida.
export const rotas = createBrowserRouter([
  { path: '/', element: <Inicio /> },
  { path: '/entrar', element: <Entrar /> },
  { path: '/primeiro-acesso', element: <PrimeiroAcesso /> },
  {
    path: '/tecnico',
    element: (
      <RotaProtegida papeis={['tecnico', 'admin']}>
        <LayoutTecnico />
      </RotaProtegida>
    ),
    children: [
      { index: true, element: <Inicio /> },
      { path: 'ordens', element: <MinhasOrdens /> },
      { path: 'ordens/:id', element: <DetalheOrdem /> },
      { path: 'ordens/:id/retirada', element: <RegistrarRetirada /> },
      { path: 'ordens/:id/visita', element: <VisitaSemSucesso /> },
      { path: 'envios', element: <Envios /> },
      { path: 'resgate', element: <Resgate /> },
    ],
  },
  {
    path: '/financeiro',
    element: (
      <RotaProtegida papeis={['financeiro', 'admin', 'estoque']}>
        <LayoutPainel />
      </RotaProtegida>
    ),
    children: [
      { path: 'ordens', element: <OrdensFinanceiro /> },
      { path: 'ordens/:id', element: <DetalheDaOrdem /> },
      { path: 'importar', element: <Importar /> },
      { path: 'repasses', element: <Repasses /> },
    ],
  },
  {
    path: '/estoque',
    element: (
      <RotaProtegida papeis={['estoque', 'admin']}>
        <LayoutPainel />
      </RotaProtegida>
    ),
    children: [{ path: 'conferir', element: <Conferir /> }],
  },
  {
    path: '/gestao',
    element: (
      <RotaProtegida papeis={['admin']}>
        <LayoutPainel />
      </RotaProtegida>
    ),
    children: [
      { path: 'equipe', element: <Equipe /> },
      { path: 'usuarios', element: <Usuarios /> },
    ],
  },
  {
    path: '/conta',
    element: (
      <RotaProtegida papeis={['admin', 'financeiro', 'tecnico', 'estoque']}>
        <LayoutDaConta />
      </RotaProtegida>
    ),
    children: [{ index: true, element: <MinhaConta /> }],
  },
  {
    path: '*',
    element: (
      <main className="mx-auto max-w-lg p-6">
        <Vazio titulo="Página não encontrada">
          Confira o endereço ou <Link to="/">volte para o início</Link>.
        </Vazio>
      </main>
    ),
  },
])
