using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Infraestrutura;
using Retirada.Comum;

namespace Retirada.Acesso.Casos;

internal sealed record UsuarioNaLista(
    Guid Id, string Nome, string Email, string Papel, string Situacao, bool DoisFatores, DateTimeOffset? UltimoLoginEm);

/// <summary>O link vai sem o domínio: o PWA monta com o endereço onde está aberto.</summary>
internal sealed record LinkDeConvite(string Caminho, DateTimeOffset ExpiraEm)
{
    public static LinkDeConvite De(Convite convite, string token) => new($"/primeiro-acesso#t={token}", convite.ExpiraEm);
}

/// <summary>RF02, só para o gestor: listar, cadastrar, convidar de novo, revogar, reativar e zerar o 2FA.</summary>
internal static class GestaoDeUsuarios
{
    internal sealed record PedidoDeCadastro(string Nome, string Email, string Papel);
    internal sealed record Cadastrado(UsuarioNaLista Usuario, LinkDeConvite Convite);

    public static void Mapear(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/usuarios").WithTags("Usuários").RequireAuthorization(Politicas.Gestao);
        grupo.MapGet("/", ListarAsync).WithSummary("Usuários e situação (RF02)");
        grupo.MapPost("/", CadastrarAsync).WithSummary("Cadastrar usuário; ele fica pendente até usar o convite (RF02)");
        grupo.MapPost("/{id:guid}/convite", NovoConviteAsync).WithSummary("Novo link de primeiro acesso ou de nova senha");
        grupo.MapPost("/{id:guid}/revogar", (Guid id, ClaimsPrincipal s, AcessoDbContext db, TimeProvider r, HttpContext h, CancellationToken ct) =>
            MudarAsync(id, s, db, r, h, "revogar_usuario", (u, agora) => u.Revogar(s.Id(), agora), ct)).WithSummary("Revogar: corta o acesso na hora (RF02)");
        grupo.MapPost("/{id:guid}/reativar", (Guid id, ClaimsPrincipal s, AcessoDbContext db, TimeProvider r, HttpContext h, CancellationToken ct) =>
            MudarAsync(id, s, db, r, h, "reativar_usuario", (u, agora) => u.Reativar(agora), ct)).WithSummary("Reativar usuário revogado");
        grupo.MapPost("/{id:guid}/zerar-2fa", (Guid id, ClaimsPrincipal s, AcessoDbContext db, TimeProvider r, HttpContext h, CancellationToken ct) =>
            MudarAsync(id, s, db, r, h, "zerar_2fa", (u, agora) => u.Zerar2FA(agora), ct)).WithSummary("Zerar o 2FA de quem trocou de celular");
    }

    private static async Task<Ok<List<UsuarioNaLista>>> ListarAsync(AcessoDbContext db, CancellationToken ct)
    {
        var usuarios = await db.Usuarios.AsNoTracking().OrderBy(u => u.Nome).ToListAsync(ct);
        return TypedResults.Ok(usuarios.Select(NaLista).ToList());
    }

    private static async Task<Results<Created<Cadastrado>, ProblemHttpResult>> CadastrarAsync(
        PedidoDeCadastro pedido, ClaimsPrincipal sessao, AcessoDbContext db, TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var email = Usuario.NormalizarEmail(pedido.Email);
        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct))
            return Sessao.Recusa("Já existe um usuário com esse e-mail.", "USUARIO_DUPLICADO", StatusCodes.Status409Conflict);

        var usuario = Usuario.Cadastrar(pedido.Nome, pedido.Email, pedido.Papel, sessao.Id(), agora);
        var (convite, token) = Convite.Gerar(usuario.Id, sessao.Id(), agora);
        db.Usuarios.Add(usuario);
        db.Convites.Add(convite);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), $"cadastrar_usuario:{usuario.Id}", Sessao.Ip(http), ct);

        return TypedResults.Created($"/api/usuarios/{usuario.Id}", new Cadastrado(NaLista(usuario), LinkDeConvite.De(convite, token)));
    }

    private static async Task<Results<Ok<LinkDeConvite>, NotFound, ProblemHttpResult>> NovoConviteAsync(
        Guid id, ClaimsPrincipal sessao, AcessoDbContext db, TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return TypedResults.NotFound();
        if (usuario.Situacao == SituacaoUsuario.Revogado)
            return Sessao.Recusa("Reative o usuário antes de mandar um convite.", "ACESSO_REVOGADO", StatusCodes.Status422UnprocessableEntity);

        var (convite, token) = Convite.Gerar(usuario.Id, sessao.Id(), relogio.GetUtcNow());
        db.Convites.Add(convite);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), $"convite_usuario:{usuario.Id}", Sessao.Ip(http), ct);
        return TypedResults.Ok(LinkDeConvite.De(convite, token));
    }

    private static async Task<Results<Ok<UsuarioNaLista>, NotFound>> MudarAsync(
        Guid id, ClaimsPrincipal sessao, AcessoDbContext db, TimeProvider relogio, HttpContext http,
        string acao, Action<Usuario, DateTimeOffset> mudanca, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return TypedResults.NotFound();

        mudanca(usuario, relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), $"{acao}:{usuario.Id}", Sessao.Ip(http), ct);
        return TypedResults.Ok(NaLista(usuario));
    }

    private static UsuarioNaLista NaLista(Usuario u) =>
        new(u.Id, u.Nome, u.Email, u.Papel, u.Situacao.ToString().ToLowerInvariant(), u.Tem2FA, u.UltimoLoginEm);
}
