using System.Buffers.Binary;
using Extrato.Application;

namespace Extrato.Infrastructure.Imagem;

/// <summary>
/// Defesa em profundidade no servidor, em C# puro (sem dependência nativa):
/// valida o formato (JPEG/PNG), confere as dimensões e remove todo metadado
/// (EXIF/GPS/XMP/ICC/comentários) reescrevendo os segmentos em memória. Nada toca o disco.
/// O redimensionamento para 1024 px acontece no cliente (canvas do navegador). Ver docs/adr/0004.
/// </summary>
public class SanitizadorImagem : IProcessadorImagem
{
    public const long BytesMaximos = 15 * 1024 * 1024;
    public const int LadoMaximo = 8192;

    private static ReadOnlySpan<byte> AssinaturaPng => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public async Task<MemoryStream> PrepararAsync(Stream entrada, CancellationToken ct)
    {
        var bytes = await LerComLimiteAsync(entrada, ct);
        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) return new MemoryStream(LimparJpeg(bytes), writable: false);
        if (bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(AssinaturaPng)) return new MemoryStream(LimparPng(bytes), writable: false);
        throw new ValidacaoException("Formato não suportado: envie JPEG ou PNG.");
    }

    public static string MimeDe(ReadOnlySpan<byte> inicio) =>
        inicio.Length > 8 && inicio[..8].SequenceEqual(AssinaturaPng) ? "image/png" : "image/jpeg";

    private static async Task<byte[]> LerComLimiteAsync(Stream entrada, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await entrada.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > BytesMaximos) throw new ValidacaoException($"Imagem maior que {BytesMaximos / 1024 / 1024} MB.");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    /// <summary>Remove APP1–APP15 (EXIF, XMP, ICC, IPTC…) e COM; mantém só o APP0/JFIF.</summary>
    internal static byte[] LimparJpeg(byte[] b)
    {
        using var saida = new MemoryStream(b.Length);
        saida.Write(b, 0, 2); // SOI
        var i = 2;
        var achouSof = false;
        while (i + 4 <= b.Length)
        {
            if (b[i] != 0xFF) throw Invalida();
            var marcador = b[i + 1];
            if (marcador == 0xFF) { i++; continue; } // preenchimento
            if (marcador is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { saida.Write(b, i, 2); i += 2; continue; }

            var tamanho = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i + 2));
            if (tamanho < 2 || i + 2 + tamanho > b.Length) throw Invalida();

            if (marcador == 0xDA) // SOS: daqui até o fim são dados comprimidos
            {
                if (!achouSof) throw Invalida();
                saida.Write(b, i, b.Length - i);
                return saida.ToArray();
            }

            if (marcador is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var altura = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i + 5));
                var largura = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i + 7));
                ValidarDimensoes(largura, altura);
                achouSof = true;
            }

            var ehJfif = marcador == 0xE0 && tamanho >= 7 && b.AsSpan(i + 4, 5).SequenceEqual("JFIF\0"u8);
            var descartar = (marcador is >= 0xE0 and <= 0xEF && !ehJfif) || marcador == 0xFE;
            if (!descartar) saida.Write(b, i, 2 + tamanho);
            i += 2 + tamanho;
        }
        throw Invalida();
    }

    /// <summary>Mantém só os chunks necessários para desenhar a imagem.</summary>
    internal static byte[] LimparPng(byte[] b)
    {
        string[] manter = ["IHDR", "PLTE", "IDAT", "IEND", "tRNS"];
        using var saida = new MemoryStream(b.Length);
        saida.Write(b, 0, 8);
        var i = 8;
        var primeiro = true;
        while (i + 12 <= b.Length)
        {
            var tamanho = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(i));
            if (tamanho < 0 || i + 12L + tamanho > b.Length) throw Invalida();
            var tipo = System.Text.Encoding.ASCII.GetString(b, i + 4, 4);
            if (primeiro)
            {
                if (tipo != "IHDR") throw Invalida();
                ValidarDimensoes(BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(i + 8)), BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(i + 12)));
                primeiro = false;
            }
            if (manter.Contains(tipo)) saida.Write(b, i, 12 + tamanho);
            i += 12 + tamanho;
            if (tipo == "IEND") return saida.ToArray();
        }
        throw Invalida();
    }

    private static void ValidarDimensoes(int largura, int altura)
    {
        if (largura <= 0 || altura <= 0) throw Invalida();
        if (largura > LadoMaximo || altura > LadoMaximo) throw new ValidacaoException("Imagem com resolução grande demais.");
    }

    private static ValidacaoException Invalida() => new("Arquivo não é uma imagem válida.");
}
