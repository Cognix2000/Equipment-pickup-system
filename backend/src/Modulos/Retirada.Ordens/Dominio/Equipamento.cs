using Retirada.Comum;

namespace Retirada.Ordens.Dominio;

/// <summary>Item da ordem. Fonte não tem série; os outros tipos são patrimônio e têm.</summary>
public sealed class Equipamento : Entidade
{
    public TipoEquipamento Tipo { get; private set; }
    // Stryker disable once String : valor inicial só para o EF Core; a fábrica sempre preenche
    public string Modelo { get; private set; } = string.Empty;
    public NumeroSerie? NumeroSerie { get; private set; }
    public string? Mac { get; private set; }
    public decimal? Valor { get; private set; } // valor do termo ou da OS de instalação (cl. 15.1)
    public SituacaoEquipamento Situacao { get; private set; } = SituacaoEquipamento.Pendente;
    public Guid? ConferidoPor { get; private set; }
    public DateTimeOffset? ConferidoEm { get; private set; }

    public bool EhPatrimonio => Tipo != TipoEquipamento.Fonte;

    private Equipamento() { } // EF Core

    public static Equipamento Criar(TipoEquipamento tipo, string modelo, NumeroSerie? numeroSerie, string? mac = null, decimal? valor = null)
    {
        if (valor is < 0)
            throw new RegraDeNegocioException("VALOR", "O valor do equipamento não pode ser negativo.");
        if (tipo != TipoEquipamento.Fonte && numeroSerie is null)
            throw new RegraDeNegocioException("SERIE", $"{tipo} precisa de número de série.");
        if (string.IsNullOrWhiteSpace(modelo))
            throw new RegraDeNegocioException("MODELO", "Informe marca e modelo do equipamento.");

        return new Equipamento { Tipo = tipo, Modelo = modelo.Trim(), NumeroSerie = numeroSerie, Mac = mac, Valor = valor };
    }

    /// <summary>Série diferente fica como divergência para o estoque (UC04), sem impedir a retirada.</summary>
    internal void RegistrarRetirada(NumeroSerie? serieLida) =>
        Situacao = Confere(serieLida) ? SituacaoEquipamento.Retirado : SituacaoEquipamento.Divergente;

    internal bool Conferir(NumeroSerie? serieLida, Guid conferidoPor, DateTimeOffset agora)
    {
        var confere = Confere(serieLida);
        Situacao = confere ? SituacaoEquipamento.Conferido : SituacaoEquipamento.Divergente;
        ConferidoPor = conferidoPor;
        ConferidoEm = agora;
        return confere;
    }

    internal void RegistrarDevolucaoNaLoja() => Situacao = SituacaoEquipamento.DevolvidoNaLoja;

    private bool Confere(NumeroSerie? serieLida) => !EhPatrimonio || serieLida == NumeroSerie;
}
