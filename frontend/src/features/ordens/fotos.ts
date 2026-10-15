import { z } from 'zod'
import { enviarArquivo } from '@/lib/api'

const fotoEnviada = z.object({ id: z.uuid(), largura: z.number(), altura: z.number(), bytes: z.number() })

/** Lado maior da foto guardada: a etiqueta continua legível e o envio cabe num sinal fraco. */
const LADO_MAXIMO = 1600
const QUALIDADE = 0.8

/**
 * RN14 e LGPD: redesenha a foto num canvas antes de enviar. Isso reduz o arquivo (de alguns MB para algumas
 * centenas de KB) e descarta o EXIF, com GPS, modelo do celular e afins. O servidor tira de novo, por garantia.
 */
export async function prepararFoto(arquivo: Blob): Promise<Blob> {
  const imagem = await createImageBitmap(arquivo, { imageOrientation: 'from-image' })
  const escala = Math.min(1, LADO_MAXIMO / Math.max(imagem.width, imagem.height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(imagem.width * escala)
  canvas.height = Math.round(imagem.height * escala)
  canvas.getContext('2d')!.drawImage(imagem, 0, 0, canvas.width, canvas.height)
  imagem.close()
  return new Promise((pronto, falhou) =>
    canvas.toBlob((jpeg) => (jpeg ? pronto(jpeg) : falhou(new Error('Não foi possível preparar a foto.'))), 'image/jpeg', QUALIDADE),
  )
}

/** Envia uma foto já preparada. Cada foto vai sozinha: se o sinal cair, só ela precisa ir de novo. */
export const enviarFoto = (ordemId: string, foto: Blob) => enviarArquivo(`/api/ordens/${ordemId}/fotos`, foto, fotoEnviada, 'foto', 'foto.jpg')

export const enderecoDaFoto = (ordemId: string, fotoId: string) => `/api/ordens/${ordemId}/fotos/${fotoId}`
