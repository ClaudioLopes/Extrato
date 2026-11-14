using Extrato.Infrastructure.Dados;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Tests;

/// <summary>
/// Banco dos testes: SQLite de verdade, em memória, com as migrations aplicadas. Cada teste ganha o seu.
/// Consultas que o SQLite não traduz (ou índices únicos violados) falham aqui como falhariam em produção.
/// </summary>
public static class BancoTeste
{
    public static ExtratoDbContext Criar()
    {
        var conexao = new SqliteConnection("DataSource=:memory:");
        conexao.Open(); // o banco em memória vive enquanto a conexão estiver aberta
        var db = new ExtratoDbContext(new DbContextOptionsBuilder<ExtratoDbContext>()
            .UseSqlite(conexao, contextOwnsConnection: true).Options);
        db.Database.Migrate();
        return db;
    }
}
