using System.Globalization;
using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Servicos;

public record MatchAlimento(Alimento Alimento, double Score);

public class AlimentoService(IAppDbContext db)
{
    /// <summary>Abaixo disso o item fica "não identificado" e o usuário escolhe.</summary>
    public const double LimiarMatch = 0.45;

    private List<(Alimento Alimento, string[] Termos)>? _indice;

    public async Task<IReadOnlyList<AlimentoDto>> BuscarAsync(string? busca, int limite, CancellationToken ct)
    {
        limite = Math.Clamp(limite, 1, 100);
        if (string.IsNullOrWhiteSpace(busca))
            return (await db.Alimentos.OrderBy(a => a.Nome).Take(limite).ToListAsync(ct)).Select(a => a.ParaDto()).ToList();

        var resultados = await RankearAsync(busca, ct);
        return resultados.Where(r => r.Score >= 0.2).Take(limite).Select(r => r.Alimento.ParaDto(Math.Round(r.Score, 3))).ToList();
    }

    /// <summary>
    /// Seção 9.4: normaliza, procura em AlimentoSinonimo e depois busca aproximada por trigramas.
    /// </summary>
    public async Task<MatchAlimento?> MatchAsync(string nomeIA, string? preparo, CancellationToken ct)
    {
        var candidatos = new[] { nomeIA, string.IsNullOrWhiteSpace(preparo) ? null : $"{nomeIA} {preparo}" }
            .Where(c => c != null).Cast<string>();
        MatchAlimento? melhor = null;
        foreach (var c in candidatos)
        {
            var top = (await RankearAsync(c, ct)).FirstOrDefault();
            if (top is not null && (melhor is null || top.Score > melhor.Score)) melhor = top;
        }
        return melhor is not null && melhor.Score >= LimiarMatch ? melhor : null;
    }

    public async Task<List<MatchAlimento>> RankearAsync(string consulta, CancellationToken ct)
    {
        var q = TextoBusca.Normalizar(consulta);
        if (q.Length == 0) return [];
        var indice = await IndiceAsync(ct);

        var lista = new List<MatchAlimento>(indice.Count);
        foreach (var (alimento, termos) in indice)
        {
            var score = TextoBusca.Pontuar(q, termos);
            if (score > 0) lista.Add(new MatchAlimento(alimento, score));
        }
        // empate: prefere nome mais curto (mais genérico)
        return lista.OrderByDescending(m => m.Score).ThenBy(m => m.Alimento.Nome.Length).ToList();
    }

    /// <summary>Catálogo completo (com sinônimos) para a busca offline do app.</summary>
    public async Task<CatalogoAlimentosDto> CatalogoAsync(CancellationToken ct)
    {
        var alimentos = await db.Alimentos.AsNoTracking().Include(a => a.Sinonimos).OrderBy(a => a.Id).ToListAsync(ct);
        var itens = alimentos.Select(a => new AlimentoCatalogoDto(a.ParaDto(),
            [a.NomeNormalizado, .. a.Sinonimos.Select(s => s.Termo)])).ToList();
        // versão muda quando qualquer alimento muda (nome, valores ou sinônimos)
        var assinatura = string.Join("|", itens.Select(i => $"{i.Alimento.Id}:{i.Alimento.Nome}:{i.Alimento.Kcal100g}:{i.Alimento.Proteina100g}:{i.Alimento.Carbo100g}:{i.Alimento.Gordura100g}:{string.Join(",", i.Termos)}"));
        var versao = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assinatura)))[..16];
        return new CatalogoAlimentosDto(versao, itens);
    }

    public async Task<AlimentoDto> CriarAsync(NovoAlimentoDto dto, CancellationToken ct)
    {
        ValidarMacros(dto.Kcal100g, dto.Proteina100g, dto.Carbo100g, dto.Gordura100g);
        var normalizado = TextoBusca.Normalizar(dto.Nome);
        if (await db.Alimentos.AnyAsync(a => a.NomeNormalizado == normalizado, ct))
            throw new ValidacaoException("Já existe um alimento com esse nome.");

        var a = new Alimento
        {
            Nome = dto.Nome.Trim(), NomeNormalizado = normalizado, Fonte = FonteAlimento.Personalizado,
            Kcal100g = dto.Kcal100g, Proteina100g = dto.Proteina100g, Carbo100g = dto.Carbo100g,
            Gordura100g = dto.Gordura100g, Fibra100g = dto.Fibra100g,
            Sinonimos = dto.Sinonimos.Select(TextoBusca.Normalizar).Where(s => s.Length > 0).Distinct()
                .Select(s => new AlimentoSinonimo { Termo = s }).ToList()
        };
        db.Alimentos.Add(a);
        await db.SaveChangesAsync(ct);
        _indice = null;
        return a.ParaDto();
    }

    /// <summary>
    /// Importa a TACO (ou outra tabela) em CSV: nome;kcal;proteina;carboidrato;gordura;fibra.
    /// Separador ';' e vírgula decimal, como nas planilhas brasileiras. Linhas existentes são atualizadas.
    /// </summary>
    public async Task<ImportacaoDto> ImportarCsvAsync(Stream csv, FonteAlimento fonte, CancellationToken ct)
    {
        var erros = new List<string>();
        int inseridos = 0, atualizados = 0, linha = 0;
        var existentes = await db.Alimentos.ToDictionaryAsync(a => a.NomeNormalizado, ct);
        using var reader = new StreamReader(csv);
        string? texto;
        var ptBr = CultureInfo.GetCultureInfo("pt-BR");
        while ((texto = await reader.ReadLineAsync(ct)) != null)
        {
            linha++;
            if (string.IsNullOrWhiteSpace(texto)) continue;
            var campos = texto.Split(';');
            if (linha == 1 && !decimal.TryParse(campos.ElementAtOrDefault(1), NumberStyles.Number, ptBr, out _)) continue; // cabeçalho
            if (campos.Length < 5) { erros.Add($"Linha {linha}: esperado nome;kcal;proteina;carbo;gordura[;fibra]"); continue; }

            decimal? Num(int i)
            {
                var s = campos.ElementAtOrDefault(i)?.Trim();
                if (string.IsNullOrEmpty(s) || s is "NA" or "Tr" or "*" or "-") return s is "Tr" ? 0 : null;
                return decimal.TryParse(s, NumberStyles.Number, ptBr, out var v) ? v : null;
            }

            var nome = campos[0].Trim().Trim('"');
            var kcal = Num(1); var prot = Num(2); var carb = Num(3); var gord = Num(4);
            if (nome.Length == 0 || kcal is null || prot is null || carb is null || gord is null)
            {
                erros.Add($"Linha {linha}: valores inválidos");
                continue;
            }

            var norm = TextoBusca.Normalizar(nome);
            if (existentes.TryGetValue(norm, out var a))
            {
                a.Kcal100g = kcal.Value; a.Proteina100g = prot.Value; a.Carbo100g = carb.Value; a.Gordura100g = gord.Value;
                a.Fibra100g = Num(5);
                atualizados++;
            }
            else
            {
                a = new Alimento
                {
                    Nome = nome, NomeNormalizado = norm, Fonte = fonte, Kcal100g = kcal.Value,
                    Proteina100g = prot.Value, Carbo100g = carb.Value, Gordura100g = gord.Value, Fibra100g = Num(5)
                };
                db.Alimentos.Add(a);
                existentes[norm] = a;
                inseridos++;
            }
        }
        await db.SaveChangesAsync(ct);
        _indice = null;
        return new ImportacaoDto(inseridos, atualizados, erros.Take(50).ToList());
    }

    private static void ValidarMacros(decimal kcal, decimal p, decimal c, decimal g)
    {
        if (p + c + g > 100) throw new ValidacaoException("Proteína + carboidrato + gordura não podem passar de 100 g por 100 g.");
        var estimado = p * 4 + c * 4 + g * 9;
        if (kcal > 0 && Math.Abs(estimado - kcal) > Math.Max(40, kcal * 0.25m))
            throw new ValidacaoException($"Calorias ({kcal}) não batem com os macros (~{estimado:0} kcal). Confira o rótulo.");
    }

    private async Task<List<(Alimento, string[])>> IndiceAsync(CancellationToken ct)
    {
        if (_indice != null) return _indice;
        var alimentos = await db.Alimentos.AsNoTracking().Include(a => a.Sinonimos).ToListAsync(ct);
        _indice = alimentos.Select(a => (a, new[] { a.NomeNormalizado }.Concat(a.Sinonimos.Select(s => s.Termo)).ToArray())).ToList();
        return _indice;
    }
}
