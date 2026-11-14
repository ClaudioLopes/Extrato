# ADR 0005 — SQLite no lugar do SQL Server

**Status:** aceito (substitui a parte de banco da [ADR 0004](0004-sql-server-e-imagem-sem-dependencia-nativa.md))

## Contexto
O app vai para o Fly.io. O SQL Server só roda em x86, pede pelo menos 2 GB de RAM e precisaria de uma
máquina própria com volume (cerca de 3 a 4 vezes o custo do app sozinho); a edição Developer, usada na demo
Docker, não pode ser usada em produção. O extrato é pessoal: um usuário, poucas centenas de gravações por mês.

## Decisão
**SQLite** num arquivo (`/data/extrato.db` no container, `extrato.db` em desenvolvimento), via EF Core.

- Uma máquina só: o arquivo fica no volume, junto com as chaves do cookie de sessão (`/data/chaves`).
- Modo WAL (`PRAGMA journal_mode=WAL` na inicialização): leituras não esperam a escrita.
- `decimal` é gravado como `REAL` (conversão global para `double`): o SQLite não tem decimal, e guardado
  como texto não dá para comparar, ordenar nem somar dentro da consulta. Kcal, gramas e kg cabem com folga.
- Índices únicos filtrados (`IdCliente`) continuam, como índices parciais do SQLite.
- Migrations recriadas do zero (`Inicial`): as do SQL Server não servem para o SQLite.
- A busca por trigramas em C# (ADR 0004) continua igual.

## Consequências
- Os testes da Application rodam em SQLite em memória com as migrations aplicadas (`BancoTeste`), no lugar do
  EF InMemory: consultas que o SQLite não traduz e violações de índice único aparecem nos testes.
- Backup: snapshots diários do volume no Fly.io; o arquivo pode ser copiado com o app parado.
- Um único processo escreve no banco: o app não escala horizontalmente (não precisa).
- Dados de um banco SQL Server antigo não são migrados automaticamente.
- O container começa como root só para dar `/data` ao usuário do app (volumes do Fly chegam como root) e
  roda o app sem root (`setpriv` no `docker-entrypoint.sh`).
