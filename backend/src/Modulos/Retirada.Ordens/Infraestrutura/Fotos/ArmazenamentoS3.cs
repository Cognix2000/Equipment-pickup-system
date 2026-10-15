using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Infraestrutura.Fotos;

/// <summary>
/// Fotos num bucket S3. Em homologação é o MinIO do docker-compose; em produção, qualquer serviço compatível
/// com S3, trocando só a configuração. O bucket não é público: quem vê a foto é a API, depois de conferir a permissão.
/// </summary>
internal sealed partial class ArmazenamentoS3(IAmazonS3 s3, string bucket, ILogger<ArmazenamentoS3>? log = null) : IArmazenamentoDeFotos
{
    /// <summary>Foto enviada e não usada numa retirada some sozinha depois deste prazo.</summary>
    public const int DiasDaFotoPendente = 2;

    private readonly SemaphoreSlim _preparo = new(1, 1);
    private bool _preparado;

    public async Task GuardarAsync(string chave, byte[] conteudo, IReadOnlyDictionary<string, string> metadados, CancellationToken ct)
    {
        await PrepararAsync(ct);
        var pedido = new PutObjectRequest
        {
            BucketName = bucket,
            Key = chave,
            InputStream = new MemoryStream(conteudo, writable: false),
            ContentType = "image/jpeg",
            AutoCloseStream = true,
        };
        foreach (var (nome, valor) in metadados) pedido.Metadata[nome] = valor;
        await s3.PutObjectAsync(pedido, ct);
    }

    public async Task<IReadOnlyDictionary<string, string>?> MetadadosAsync(string chave, CancellationToken ct)
    {
        try
        {
            var resposta = await s3.GetObjectMetadataAsync(bucket, chave, ct);
            // O S3 devolve as chaves como "x-amz-meta-autor"; aqui volta só "autor".
            return resposta.Metadata.Keys.ToDictionary(
                k => k.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase) ? k["x-amz-meta-".Length..] : k,
                k => resposta.Metadata[k]);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task CopiarAsync(string de, string para, CancellationToken ct) =>
        s3.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucket,
            SourceKey = de,
            DestinationBucket = bucket,
            DestinationKey = para,
            MetadataDirective = S3MetadataDirective.COPY,
        }, ct);

    public async Task<Stream?> AbrirAsync(string chave, CancellationToken ct)
    {
        try
        {
            var resposta = await s3.GetObjectAsync(bucket, chave, ct);
            return resposta.ResponseStream;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task ApagarAsync(string chave, CancellationToken ct) => s3.DeleteObjectAsync(bucket, chave, ct);

    /// <summary>
    /// Na primeira gravação, cria o bucket se faltar e pede ao S3 que apague as fotos pendentes antigas
    /// (enviadas e nunca usadas numa retirada). Sem isso, elas ficariam para sempre.
    /// </summary>
    private async Task PrepararAsync(CancellationToken ct)
    {
        if (_preparado) return;
        await _preparo.WaitAsync(ct);
        try
        {
            if (_preparado) return;
            try
            {
                await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);
            }
            catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
            }
            try
            {
                await ExpirarPendentesAsync(ct);
            }
            catch (AmazonS3Exception e)
            {
                // Sem a regra, as fotos continuam sendo guardadas; só as pendentes esquecidas ficam para trás.
                if (log is not null) RegraDeExpiracaoFalhou(log, e, bucket);
            }
            _preparado = true;
        }
        finally
        {
            _preparo.Release();
        }
    }

    private Task ExpirarPendentesAsync(CancellationToken ct) =>
        s3.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
        {
            BucketName = bucket,
            Configuration = new LifecycleConfiguration
            {
                Rules =
                [
                    new LifecycleRule
                    {
                        Id = "fotos-pendentes",
                        Status = LifecycleRuleStatus.Enabled,
                        Filter = new LifecycleFilter { LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = Chaves.PrefixoPendente } },
                        Expiration = new LifecycleRuleExpiration { Days = DiasDaFotoPendente },
                    },
                ],
            },
        }, ct);

    [LoggerMessage(Level = LogLevel.Warning, Message = "O armazenamento não aceitou a regra que apaga as fotos pendentes do bucket {Bucket}; elas vão se acumular.")]
    private static partial void RegraDeExpiracaoFalhou(ILogger logger, Exception erro, string bucket);
}
