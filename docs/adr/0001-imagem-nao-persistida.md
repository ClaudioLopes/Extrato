# ADR 0001 — A imagem não é persistida (e isso não basta para privacidade)

**Status:** aceito

## Contexto
O registro de refeições por foto é a funcionalidade central. Fotos carregam metadados
(EXIF com GPS, modelo do aparelho, horário) e, no caso de fotos do corpo, dados sensíveis.

## Decisão
- Nenhuma tabela tem coluna de imagem. Só os dados extraídos e **confirmados** ficam no banco.
- O upload é lido como `Stream` direto do `multipart/form-data` (`MultipartReader`), sem
  `IFormFile`, que pode bufferizar em disco.
- A imagem é processada em memória, enviada ao provedor e sai de escopo ao fim da requisição.
- Nenhum log contém o conteúdo ou o base64 da imagem.
- O servidor não guarda estado entre "analisar" e "confirmar": o rascunho volta ao cliente.

## Por que não basta
Não guardar a foto no **nosso** banco não impede o **provedor** de guardar. Nos termos do
Gemini API, o conteúdo enviado no uso gratuito pode ser usado para melhorar produtos, inclusive
com leitura por revisores humanos. Por isso:
- Foto do prato: risco baixo, pode usar o tier grátis.
- Foto do corpo: **não** é enviada ao tier grátis. A avaliação corporal usa só medidas
  (método da Marinha americana) e observações em texto.

## Atualização: provedor Claude
Com o `ClaudeVisionProvider` (API paga da Anthropic), o envio não cai nos termos do tier grátis do Gemini.
Mesmo assim, a foto do corpo continua fora da fase 1: a regra é não enviar dado sensível sem necessidade,
e o cálculo das metas usa medidas, não fotos. Antes de liberar foto do corpo na fase 2, confira os termos
comerciais e a política de retenção de dados vigentes da API.
