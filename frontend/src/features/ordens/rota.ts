import type { Ordem } from './tipos'

/**
 * Com coordenada (a ordem já teve retirada ou visita com GPS), a rota vai ao ponto exato; sem ela, ao endereço
 * escrito, com o bairro para o app de mapas não errar de cidade vizinha. A planilha do HubSoft não traz coordenada.
 */
export function linksDeRota(ordem: Ordem) {
  const { endereco, localizacao } = ordem
  if (localizacao) {
    const ponto = `${localizacao.latitude},${localizacao.longitude}`
    return {
      googleMaps: `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(ponto)}`,
      waze: `https://waze.com/ul?ll=${encodeURIComponent(ponto)}&navigate=yes`,
    }
  }
  const texto = [`${endereco.logradouro}${endereco.numero ? `, ${endereco.numero}` : ''}`, endereco.bairro, `${endereco.cidade} - ${endereco.uf}`].join(', ')
  return {
    googleMaps: `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(texto)}`,
    waze: `https://waze.com/ul?q=${encodeURIComponent(texto)}&navigate=yes`,
  }
}
