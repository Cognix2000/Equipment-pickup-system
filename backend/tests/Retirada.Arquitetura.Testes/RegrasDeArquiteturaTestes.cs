using System.Reflection;
using NetArchTest.Rules;
using Retirada.Comum;
using Retirada.Ordens;
using Shouldly;

namespace Retirada.Arquitetura.Testes;

/// <summary>
/// ADR-003: as fronteiras do modelo híbrido viram teste. Se alguém quebrar uma regra,
/// o build quebra e a mensagem diz qual tipo saiu da linha.
/// </summary>
public class RegrasDeArquiteturaTestes
{
    private static readonly string[] Frameworks = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql"];

    /// <summary>Todo módulo novo entra nesta lista; o resto dos testes vale para ele sozinho.</summary>
    private static readonly Assembly[] Modulos = [typeof(ModuloOrdens).Assembly, typeof(Retirada.Acesso.ModuloAcesso).Assembly];

    public static TheoryData<string> NomesDosModulos()
    {
        var dados = new TheoryData<string>();
        foreach (var m in Modulos) dados.Add(m.GetName().Name!);
        return dados;
    }

    private static Assembly Modulo(string nome) => Modulos.Single(m => m.GetName().Name == nome);

    [Theory]
    [MemberData(nameof(NomesDosModulos))]
    public void Dominio_nao_conhece_banco_nem_web(string modulo)
    {
        var resultado = Types.InAssembly(Modulo(modulo))
            .That().ResideInNamespace($"{modulo}.Dominio")
            .ShouldNot().HaveDependencyOnAny([.. Frameworks, $"{modulo}.Infraestrutura", $"{modulo}.Casos"])
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(Falhas(resultado));
    }

    [Theory]
    [MemberData(nameof(NomesDosModulos))]
    public void Casos_de_uso_ficam_dentro_do_modulo(string modulo)
    {
        var resultado = Types.InAssembly(Modulo(modulo))
            .That().ResideInNamespace($"{modulo}.Casos")
            .ShouldNot().BePublic()
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(Falhas(resultado));
    }

    [Fact]
    public void Modulo_so_usa_o_contrato_de_outro_modulo()
    {
        foreach (var modulo in Modulos)
        {
            var interioresDosOutros = Modulos
                .Where(outro => outro != modulo)
                .SelectMany(outro => new[] { "Dominio", "Casos", "Infraestrutura", "Portas" }.Select(p => $"{outro.GetName().Name}.{p}"))
                .ToArray();
            if (interioresDosOutros.Length == 0) continue;

            var resultado = Types.InAssembly(modulo).ShouldNot().HaveDependencyOnAny(interioresDosOutros).GetResult();
            resultado.IsSuccessful.ShouldBeTrue(Falhas(resultado));
        }
    }

    [Fact]
    public void Base_comum_nao_depende_de_modulo_nem_de_framework()
    {
        var resultado = Types.InAssembly(typeof(Entidade).Assembly)
            .ShouldNot().HaveDependencyOnAny([.. Frameworks, .. Modulos.Select(m => m.GetName().Name!)])
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(Falhas(resultado));
    }

    private static string Falhas(TestResult resultado) =>
        "Fora da regra: " + string.Join(", ", resultado.FailingTypeNames ?? []);
}
