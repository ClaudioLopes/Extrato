# Publicar no Fly.io

Uma máquina (`shared-cpu-1x`, 512 MB, região `gru`) com o app e o banco SQLite num volume de 1 GB
([ADR 0005](adr/0005-sqlite-no-lugar-do-sql-server.md)). O HTTPS no endereço `https://<app>.fly.dev` é do Fly.
Confira os preços atuais em <https://fly.io/docs/about/pricing/> antes de criar.

Todos os comandos rodam na raiz do repositório, com o `flyctl` logado (`fly auth login`).

## 1. Criar o app e o volume (uma vez)

```bash
fly apps create extrato
```

Se o nome já existir, escolha outro e troque `app = "extrato"` no `fly.toml`.

```bash
fly volumes create extrato_dados --region gru --size 1
```

O Fly avisa que um volume vive em um único servidor: é o esperado para SQLite. Os snapshots diários do
volume são o backup (veja a seção 4).

## 2. Segredos

O primeiro acesso (criar login e senha) exige um código, porque a requisição não vem do próprio servidor.
Use um código longo e aleatório e guarde-o só até criar a senha:

```bash
fly secrets set Auth__CodigoConfiguracao="troque-por-um-codigo-longo"
```

IA de visão (opcional; sem chave, o app usa o modo demonstração):

```bash
fly secrets set Claude__ApiKey="sk-ant-..."
```

ou `Gemini__ApiKey`. O Gemini grátis não recebe a foto do corpo (seção 2.1 da doc).

## 3. Deploy

```bash
fly deploy
```

Abra `https://<app>.fly.dev`, crie o login com o código e instale o app no celular (Compartilhar → Adicionar
à Tela de Início, no iPhone). Depois do primeiro acesso, o código pode ser removido:

```bash
fly secrets unset Auth__CodigoConfiguracao
```

## 4. Backup e restauração

- O Fly tira snapshots diários do volume: `fly volumes snapshots list <id-do-volume>` (o id sai de
  `fly volumes list`). Para restaurar, crie um volume a partir do snapshot (`fly volumes create extrato_dados
  --snapshot-id <id> --region gru --size 1`) e troque a máquina para ele.
- Cópia manual do banco (com o app servindo, o modo WAL mantém o arquivo consistente para leitura):

```bash
fly ssh sftp get /data/extrato.db extrato-backup.db
```

## 5. Atualizar

`fly deploy` de novo. As migrations rodam na inicialização; o volume, o banco e as sessões continuam.
O CI (GitHub Actions) roda os testes e o smoke test do container a cada push; o deploy continua manual.

## Observações

- A máquina fica sempre ligada (`min_machines_running = 1`): o ajuste semanal da meta roda dentro do app na
  segunda-feira, e não haveria quem o disparasse com a máquina parada.
- `Extrato__ProxyReverso=true` faz o app ler o IP do cliente e o https de `X-Forwarded-*`. Só use atrás de um
  proxy que seja o único caminho até o app, como o do Fly.
- O app no celular só troca para uma versão nova depois que todas as abas dele forem fechadas (comportamento
  padrão de PWA).
