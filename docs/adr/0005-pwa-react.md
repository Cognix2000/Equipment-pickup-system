# ADR-005 · PWA em React com o design system da Netwise

**Status:** Aceita

## Contexto

O técnico usa o celular em campo, às vezes sem sinal; o financeiro e o gestor usam o computador. As lojas de aplicativo ficam para o incremento 5.

## Decisão

PWA com React, TypeScript e Vite; Tailwind com os tokens da Netwise; componentes no padrão shadcn/ui (Radix); TanStack Query e Table; React Hook Form com Zod; MapLibre; Lucide e Motion; vite-plugin-pwa; Storybook.

## Alternativas descartadas

- App nativo desde o início: dois códigos e publicação em loja antes de validar o fluxo.
- Biblioteca de componentes pronta e fechada (MUI, Ant): briga com a identidade da Netwise.

## Consequências

- Telas responsivas, conferidas em 390, 820 e 1280 px.
- Fontes empacotadas e mapa sem servidor externo: funciona offline.
