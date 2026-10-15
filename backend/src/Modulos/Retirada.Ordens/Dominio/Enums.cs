namespace Retirada.Ordens.Dominio;

/// <summary>Estados da ordem. Os nomes batem com o CHECK da coluna ordem.status.</summary>
public enum StatusOrdem
{
    Nova,                  // importada, dentro da carência de 48 h (RN03)
    Disponivel,
    Atribuida,
    EmRota,
    Concluida,             // equipamento retirado
    Conferida,             // conferido no estoque
    NaoConcluida,
    Cancelada,
    DevolvidaNaLoja,
    EncerradaSemRetirada,
}

public enum TipoEquipamento { ONT, ONU, Roteador, RepetidorMesh, Fonte }

public enum SituacaoEquipamento { Pendente, Retirado, DevolvidoNaLoja, Conferido, Divergente, Extraviado }

public enum MotivoCancelamento { Pagou, Renegociou, Erro }

public enum MotivoVisitaSemSucesso { Ausente, Recusou, NaoAchou }

public enum CanalContato { Ligacao, Whatsapp }

/// <summary>Os nomes batem com o CHECK de tentativa_contato.resultado.</summary>
public enum ResultadoContato
{
    // ligação
    Atendeu,
    NaoAtendeu,
    CaixaPostal,
    NumeroInexistente,
    // WhatsApp (enviado à mão pelo app oficial; o sistema não manda mensagem)
    Enviada,
    SemWhatsapp,
    NaoEntregue,
    Respondeu,
}

public enum ModeloDeMensagem { Aviso, Lembrete, Confirmacao, Ausente }

/// <summary>Os nomes batem com o CHECK de evidencia.tipo.</summary>
public enum TipoDeFoto { Equipamento, EtiquetaSerie, ComprovanteLoja }
