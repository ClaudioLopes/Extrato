using System.Text.Json;
using System.Text.Json.Serialization;
using Extrato.Contracts;
using Microsoft.JSInterop;

namespace Extrato.Web.Servicos;

/// <summary>Falha de rede (servidor inalcançável): o app entra no modo offline.</summary>
public class SemConexaoException() : ApiException("Sem conexão com o servidor.");

/// <summary>401: a sessão expirou; a fila espera o próximo login.</summary>
public class SessaoExpiradaException() : ApiException("Sessão expirada. Entre novamente.");

/// <summary>
/// Dados salvos no aparelho (localStorage) para uso offline. Tudo fica sob o prefixo
/// "extrato:" e é apagado ao sair da conta.
/// </summary>
public class ArmazenamentoLocal(IJSRuntime js)
{
    public const string Prefixo = "extrato:";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<T?> LerAsync<T>(string chave)
    {
        try
        {
            var texto = await js.InvokeAsync<string?>("extrato.ler", Prefixo + chave);
            return texto is null ? default : JsonSerializer.Deserialize<T>(texto, Json);
        }
        catch (Exception) { return default; } // JSON antigo/corrompido ou armazenamento bloqueado
    }

    public async Task<bool> GravarAsync<T>(string chave, T valor)
    {
        try { return await js.InvokeAsync<bool>("extrato.gravar", Prefixo + chave, JsonSerializer.Serialize(valor, Json)); }
        catch (Exception) { return false; }
    }

    public async Task RemoverAsync(string chave)
    {
        try { await js.InvokeVoidAsync("extrato.remover", Prefixo + chave); } catch (Exception) { }
    }

    public async Task LimparTudoAsync()
    {
        try { await js.InvokeVoidAsync("extrato.limparPrefixo", Prefixo); } catch (Exception) { }
    }
}

/// <summary>Valor salvo com o momento em que veio do servidor.</summary>
public record Salvo<T>(T Valor, DateTime SalvoEm);

public enum TipoPendente { Refeicao = 1, Peso = 2, Sessao = 3, Serie = 4 }

/// <summary>Lançamento feito sem conexão, aguardando envio.</summary>
public record Pendente(Guid Id, TipoPendente Tipo, DateTime CriadoEm, NovaRefeicaoDto? Refeicao, NovoPesoDto? Peso, RefeicaoDto? Previa)
{
    /// <summary>Sessão de treino iniciada offline (Id = IdCliente da sessão).</summary>
    public NovaSessaoDto? Sessao { get; init; }
    /// <summary>Série lançada offline (Id = IdCliente da série).</summary>
    public SeriePendente? Serie { get; init; }
    /// <summary>Série de uma sessão que já existe no servidor.</summary>
    public int? SessaoId { get; init; }
    /// <summary>Série de uma sessão iniciada offline, conhecida só pelo id gerado no aparelho.</summary>
    public Guid? SessaoIdCliente { get; init; }
}

/// <summary>Sessão de treino iniciada offline que ainda está na fila (aparece em "Sessões recentes").</summary>
public record SessaoPendente(Guid IdCliente, DateTime DataHora, int? RotinaId, int Series);

/// <summary>Estado da conexão e da fila, observado pelo layout e pelas páginas.</summary>
public class RedeEstado
{
    public bool Online { get; private set; } = true;
    public int Pendentes { get; private set; }
    public bool Sincronizando { get; private set; }
    public List<string> Rejeitados { get; } = [];

    public event Action? Mudou;
    /// <summary>Disparado quando a fila foi enviada: as páginas recarregam os dados do servidor.</summary>
    public event Action? Sincronizou;

    public void DefinirOnline(bool online)
    {
        if (Online == online) return;
        Online = online;
        Mudou?.Invoke();
    }

    public void DefinirPendentes(int n, bool sincronizando = false)
    {
        Pendentes = n;
        Sincronizando = sincronizando;
        Mudou?.Invoke();
    }

    public void Rejeitar(string mensagem)
    {
        Rejeitados.Add(mensagem);
        Mudou?.Invoke();
    }

    public void LimparRejeitados()
    {
        Rejeitados.Clear();
        Mudou?.Invoke();
    }

    public void AvisarSincronizou() => Sincronizou?.Invoke();
}
