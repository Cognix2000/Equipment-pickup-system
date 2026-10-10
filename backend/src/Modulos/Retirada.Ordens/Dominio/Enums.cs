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
