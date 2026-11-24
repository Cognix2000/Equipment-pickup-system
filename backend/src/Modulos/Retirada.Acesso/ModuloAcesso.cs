using System.Security.Claims;
using System.Threading.RateLimiting;
using Fido2NetLib;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Retirada.Acesso.Casos;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Infraestrutura;
using Retirada.Acesso.Seguranca;
using Retirada.Comum;

namespace Retirada.Acesso;

/// <summary>Porta de entrada do módulo Acesso (ADR-010).</summary>
public static class ModuloAcesso
{
    public const string LimiteDeTentativas = "tentativas_de_acesso";

    /// <summary>Cabeçalho que o PWA manda em todo comando. Outro site não consegue mandar sem CORS: barra CSRF.</summary>
    public const string CabecalhoAntiCsrf = "X-Retirada";

    public static IServiceCollection AddModuloAcesso(
        this IServiceCollection services, string connectionString, IConfiguration config, IHostEnvironment ambiente)
    {
        services.AddDbContext<AcessoDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<SegundoFator>();
        services.AddSingleton<CerimoniaDaChave>();
        services.AddSingleton<IFido2>(new Fido2(CerimoniaDaChave.Configuracao(config)));
        services.AddScoped<ServicoDeNotificacoes>();
        services.AddScoped<INotificacoesPush>(sp => sp.GetRequiredService<ServicoDeNotificacoes>());

        // As chaves cifram o cookie e o segredo do 2FA. Perdê-las derruba sessões e invalida todo 2FA:
        // fora do desenvolvimento, a pasta é obrigatória e precisa estar num volume com backup.
        var pasta = config["DataProtection:Pasta"];
        if (string.IsNullOrWhiteSpace(pasta))
        {
            if (!ambiente.IsDevelopment())
                throw new InvalidOperationException("Configure DataProtection:Pasta (volume persistente e com backup).");
            pasta = Path.Combine(Path.GetTempPath(), "retirada-chaves-dev");
        }
        services.AddDataProtection().SetApplicationName("retirada").PersistKeysToFileSystem(new DirectoryInfo(pasta));

        services.AddAuthentication(Sessao.Esquema).AddCookie(o =>
        {
            o.Cookie.Name = "retirada_sessao";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS em produção vem do Nginx (cabeçalhos repassados)
            // Sessão longa com rolagem: 30 dias sem usar o sistema; ao usar, renova para outros 30 dias.
            // Era 12 h antes de nov/2026, mas o pedido do Pedro é não precisar entrar de novo a cada expediente.
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            // API não redireciona para página de login: responde 401 e 403, e o PWA decide.
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            o.Events.OnValidatePrincipal = ValidarSessaoAsync;
        });

        services.AddAuthorizationBuilder()
            // Sem dizer nada, todo endpoint exige login completo; o que é público declara AllowAnonymous.
            .SetFallbackPolicy(Completo().Build())
            .AddPolicy(Politicas.LoginParcial, p => p.RequireAuthenticatedUser())
            .AddPolicy(Politicas.Gestao, p => Completo(p).RequireRole(Papeis.Gestor))
            .AddPolicy(Politicas.LerOrdens, p => Completo(p).RequireRole(Papeis.Gestor, Papeis.Financeiro, Papeis.Tecnico, Papeis.Estoque))
            .AddPolicy(Politicas.Financeiro, p => Completo(p).RequireRole(Papeis.Gestor, Papeis.Financeiro))
            .AddPolicy(Politicas.Campo, p => Completo(p).RequireRole(Papeis.Tecnico))
            .AddPolicy(Politicas.Estoque, p => Completo(p).RequireRole(Papeis.Gestor, Papeis.Estoque))
            .AddPolicy(Politicas.Contato, p => Completo(p).RequireRole(Papeis.Gestor, Papeis.Financeiro, Papeis.Tecnico));

        // Sem sessão o limite é por IP, e a equipe inteira pode entrar pela mesma rede do escritório
        // no começo do turno (um IP só). Os valores comportam uns 30 técnicos logando no mesmo minuto;
        // contra força bruta, quem protege cada conta é o bloqueio de 15 min após 5 erros.
        var tentativasPorMinuto = config.GetValue("Acesso:TentativasPorMinuto", 30);
        var requisicoesPorMinuto = config.GetValue("Acesso:RequisicoesPorMinuto", 300);
        var requisicoesAnonimasPorMinuto = config.GetValue("Acesso:RequisicoesAnonimasPorMinuto", 120);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Mesmo formato dos outros erros (ADR-009), com o tempo de espera para o cliente.
            o.OnRejected = (contexto, cancelamento) =>
            {
                var espera = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var tempo) ? tempo : TimeSpan.FromMinutes(1);
                contexto.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString();
                return new ValueTask(Results.Problem(
                    title: "Muitas tentativas. Espere um minuto e tente de novo.",
                    statusCode: StatusCodes.Status429TooManyRequests,
                    extensions: new Dictionary<string, object?> { ["codigo"] = "MUITAS_TENTATIVAS" })
                    .ExecuteAsync(contexto.HttpContext));
            };

            // Limite geral, em toda a API: por usuário quando há sessão, por IP quando não há.
            // Roda depois da autenticação, então uma sessão revogada já conta como anônima.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (http.Request.Path.StartsWithSegments("/health")) return RateLimitPartition.GetNoLimiter("saude");
                // O gateway manda avisos em rajada, sempre do mesmo IP; o token dele é que barra o resto.
                if (http.Request.Path.StartsWithSegments("/api/webhooks")) return RateLimitPartition.GetNoLimiter("gateway");
                var usuario = http.User.FindFirst(Reivindicacoes.Usuario)?.Value;
                return usuario is not null
                    ? PorMinuto($"usuario:{usuario}", requisicoesPorMinuto)
                    : PorMinuto($"ip:{IpDe(http)}", requisicoesAnonimasPorMinuto);
            });

            // Além do geral, login e 2FA têm um limite bem menor por IP (força bruta).
            o.AddPolicy(LimiteDeTentativas, http => PorMinuto(IpDe(http), tentativasPorMinuto));
        });

        return services;
    }

    private static string IpDe(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

    private static RateLimitPartition<string> PorMinuto(string chave, int limite) =>
        RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1) });

    private static AuthorizationPolicyBuilder Completo(AuthorizationPolicyBuilder? p = null) =>
        (p ?? new AuthorizationPolicyBuilder()).RequireAuthenticatedUser().RequireClaim(Reivindicacoes.Etapa, EtapasDeAcesso.Completo);

    /// <summary>Barra comandos sem o cabeçalho do PWA. Precisa vir antes da autenticação.</summary>
    public static IApplicationBuilder UseProtecaoCsrf(this IApplicationBuilder app) => app.Use(async (http, proximo) =>
    {
        var metodo = http.Request.Method;
        var ehComando = !(HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo));
        // Os avisos do gateway (/api/webhooks) não vêm do navegador: quem os autentica é o token do gateway.
        var ehAvisoDeGateway = http.Request.Path.StartsWithSegments("/api/webhooks");
        if (ehComando && !ehAvisoDeGateway && http.Request.Path.StartsWithSegments("/api") && http.Request.Headers[CabecalhoAntiCsrf] != "1")
        {
            await Results.Problem(title: "Requisição sem o cabeçalho de proteção do aplicativo.", statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["codigo"] = "CSRF" }).ExecuteAsync(http);
            return;
        }
        await proximo(http);
    });

    public static IEndpointRouteBuilder MapModuloAcesso(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/acesso").WithTags("Acesso");
        Entrar.Mapear(grupo);
        ConfirmarCodigo.Mapear(grupo);
        IniciarConfiguracao2FA.Mapear(grupo);
        ConfirmarConfiguracao2FA.Mapear(grupo);
        DesativarSegundoFator.Mapear(grupo);
        ChaveDeAcessoCasos.Mapear(grupo);
        InscricaoPush.Mapear(grupo);
        QuemSouEu.Mapear(grupo);
        Sair.Mapear(grupo);
        AceitarConvite.Mapear(grupo);
        GestaoDeUsuarios.Mapear(app);
        return app;
    }

    /// <summary>
    /// RF02 "revogar corta o acesso na hora": a cada requisição, confere no banco se o usuário segue ativo
    /// e se o carimbo da sessão é o atual. Uma consulta por chave primária, barata para este volume.
    /// </summary>
    private static async Task ValidarSessaoAsync(CookieValidatePrincipalContext contexto)
    {
        var principal = contexto.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(Reivindicacoes.Usuario), out var id)
            || !Guid.TryParse(principal?.FindFirstValue(Reivindicacoes.Carimbo), out var carimbo))
        {
            contexto.RejectPrincipal();
            return;
        }

        var db = contexto.HttpContext.RequestServices.GetRequiredService<AcessoDbContext>();
        var atual = await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new { u.Situacao, u.CarimboSeguranca })
            .SingleOrDefaultAsync(contexto.HttpContext.RequestAborted);

        if (atual is null || atual.Situacao != SituacaoUsuario.Ativo || atual.CarimboSeguranca != carimbo)
        {
            contexto.RejectPrincipal();
            await Sessao.FecharAsync(contexto.HttpContext);
        }
    }

    /// <summary>Homologação: dá a senha de demonstração aos usuários fictícios (@exemplo.invalid) que ainda não têm senha.</summary>
    public static async Task<int> DefinirSenhasDeDemonstracaoAsync(string connectionString, string senha, CancellationToken ct = default)
    {
        await using var conexao = new NpgsqlConnection(connectionString);
        await conexao.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("""
            UPDATE retirada.usuario
               SET senha_hash = @hash, situacao = 'ativo', atualizado_em = now()
             WHERE email LIKE '%@exemplo.invalid' AND senha_hash IS NULL
            """, conexao);
        comando.Parameters.AddWithValue("hash", HashDeSenha.Gerar(senha));
        return await comando.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Cria um usuário já ativo, com a senha definida e sem link de convite. Para o começo da operação: o gestor
    /// quer poder abrir o sistema como cada papel sem passar pelo convite. A senha passa pela mesma política do
    /// login (mínimo 12 caracteres, sem conter o e-mail).
    /// </summary>
    public static async Task CriarUsuarioAtivoAsync(string connectionString, string nome, string email, string papel, string senha, string? vinculo, CancellationToken ct = default)
    {
        HashDeSenha.ValidarPolitica(senha, email);
        var opcoes = new DbContextOptionsBuilder<AcessoDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new AcessoDbContext(opcoes);
        var agora = DateTimeOffset.UtcNow;
        var emailNormalizado = Usuario.NormalizarEmail(email);
        if (await db.Usuarios.AnyAsync(u => u.Email == emailNormalizado, ct))
            throw new RegraDeNegocioException("USUARIO_DUPLICADO", $"Já existe um usuário com o e-mail {emailNormalizado}.");
        var usuario = Usuario.CadastrarAutoaprovadoComVinculo(nome, email, papel, agora, vinculo);
        usuario.DefinirSenha(HashDeSenha.Gerar(senha), agora);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Produção: cria o primeiro gestor e devolve o link de primeiro acesso (vale 48 h).</summary>
    public static async Task<string> ConvidarPrimeiroGestorAsync(string connectionString, string nome, string email, CancellationToken ct = default)
    {
        var opcoes = new DbContextOptionsBuilder<AcessoDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new AcessoDbContext(opcoes);
        var agora = DateTimeOffset.UtcNow;
        var gestor = Usuario.CadastrarPrimeiroGestor(nome, email, agora);
        var (convite, token) = Convite.Gerar(gestor.Id, gestor.Id, agora);
        db.Usuarios.Add(gestor);
        db.Convites.Add(convite);
        await db.SaveChangesAsync(ct);
        return LinkDeConvite.De(convite, token).Caminho;
    }
}
