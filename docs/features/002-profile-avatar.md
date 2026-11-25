# Feature 002 — Perfil e avatar

- Data: 2026-11-25 · Commit: ver `git log` (Feature: 002-profile-avatar) · Agentes: claude

## O que faz
A página "Minha conta" reúne o perfil da pessoa logada. Nela ela edita o nome de exibição, envia, troca ou remove a foto de perfil (avatar) e escolhe o idioma, e tem um link para "Minhas passkeys". O avatar aceita JPEG, PNG ou WebP de até 5 MB. O servidor confere o conteúdo real do arquivo, gera uma imagem quadrada de 256x256 em WebP e descarta os metadados da foto, inclusive a localização. A imagem fica em um armazenamento privado e só a própria pessoa consegue vê-la. Quando um avatar é trocado ou removido, o arquivo anterior é apagado em segundo plano. O cabeçalho do site mostra o avatar (ou as iniciais do nome) ao lado do nome, e o clique leva à página da conta.

## O que mudou
- **API:** atualização do nome de exibição (`PUT /api/identity/account/profile`), envio de avatar (`PUT .../avatar`), remoção (`DELETE .../avatar`) e leitura do avatar (`GET .../avatar/{version}`). `GET /api/identity/account/me` passou a informar `avatarUrl`.
- **Processamento de imagem:** biblioteca SkiaSharp, em código próprio na camada Core. Decisão registrada na ADR 0004.
- **Armazenamento:** bucket privado no RustFS, criado pelo próprio sistema (`resources setup` e na subida dos serviços). Nenhum endereço público.
- **Worker:** apaga o arquivo anterior depois de uma troca ou remoção, e confere, com atraso, se o arquivo de um envio interrompido ficou órfão. A fila é `identity-avatars`.
- **Site:** avatar no cabeçalho, página "Minha conta" com formulário de nome, área de envio com arrastar e soltar, e o seletor de idioma.
- **Banco:** colunas do avatar e da versão do avatar no usuário, com migração própria.
- **Configuração:** novas chaves `Avatars__*` (limites de processamento e atraso da checagem de órfãos), documentadas em `deploy/.env.example`.

## Decisões
- **Reprocessar toda imagem no servidor** (ADR 0004): o arquivo enviado nunca é guardado como está. Ele é decodificado e gerado de novo a partir dos pixels, o que elimina metadados e conteúdos escondidos no arquivo.
- **SkiaSharp e não ImageSharp nem Magick.NET** (ADR 0004): a licença MIT permite uso comercial no template, e a variante sem dependências de sistema roda na imagem Linux padrão do .NET.
- **Imagem servida pela própria API, não por link assinado:** o armazenamento fica na rede interna, e a sessão e a autorização continuam em um único lugar. A URL leva só a versão, então o navegador pode guardar a imagem por muito tempo (`private, immutable`).
- **Versão que só cresce:** cada troca cria uma versão nova, e a versão não volta atrás, nem depois de remover o avatar. Assim, uma URL antiga nunca aponta para uma imagem nova por engano.
- **Chave gerada pelo servidor:** o caminho do arquivo tem o identificador da conta e um GUID. O nome enviado pelo cliente nunca vira caminho.
- **Escrita primeiro, depois o banco:** o arquivo é gravado antes da mudança no perfil. Se a gravação falhar, o perfil não muda. Se a mudança no banco falhar, o arquivo novo é apagado.
- **Limite global de processamento:** no máximo 2 decodificações ao mesmo tempo (cerca de 192 MB no pico), com espera de 10 s. Depois disso a resposta é `503 avatar.busy`, para proteger a memória.

## Testes
- Unitários: regras do avatar no domínio, processamento de imagem (formatos, arquivo falso com extensão de imagem, arquivo corrompido, imagens com dimensões excessivas, orientação EXIF, ausência de GPS e EXIF na saída), limite de envios e métricas.
- Integração, contra PostgreSQL, RabbitMQ, Redis, RustFS e Mailpit reais: envio, troca, remoção repetida, recusas (413, 415, 400), token antiforgery ausente, acesso não autorizado, avatar de outra pessoa inacessível, cabeçalhos de cache, `avatarUrl` no `me`, o Worker apagando o arquivo anterior e a checagem de órfãos, e falha de armazenamento sem mudar o perfil.
- Site: testes com Vitest para o cliente da API, o componente de avatar (imagem ou iniciais), a página da conta (erros do formulário, arrastar e soltar, arquivo grande, remoção) e a paridade dos três catálogos de idioma.
- Resultado: 255 testes do servidor e 131 do site passando, `./scripts/verify.sh` sem erros. A prova nas imagens Linux (`scripts/verify-images.sh`) passou em arm64: a biblioteca nativa carrega e os testes do processador rodam num container Linux. O x64 roda no CI.

## Notas de segurança
- Só o dono acessa o avatar. A chave vem sempre da linha da própria conta, nunca da URL.
- O conteúdo é validado pelos bytes do arquivo, não pela extensão ou pelo tipo informado. O arquivo é recusado antes de ser decodificado se as dimensões forem grandes demais.
- EXIF, XMP e localização (GPS) são removidos, porque a imagem é gerada de novo a partir dos pixels.
- Envios têm limite de tamanho no transporte e no código, e limite de frequência de 10 envios a cada 10 minutos por conta.
- O bucket é privado: sem política pública, sem ACL e sem link assinado.
- Os logs guardam só o identificador da conta e o do objeto, nunca o nome de arquivo nem o conteúdo.
- **Riscos aceitos:**
  - a API transfere os bytes da imagem. Com 256x256 (10 a 30 KB) e cache privado, o custo é baixo. Um CDN futuro poderá trocar para links assinados mantendo o mesmo contrato `avatarUrl`;
  - sob concorrência, a última gravação vence, e os arquivos substituídos são sempre apagados.

## Pendências
- Testar à mão no navegador: arrastar e soltar, tela de 320px e leitura do aviso de erro por leitor de tela.
- Ver o job de imagens x64 passar no CI.
- Conferir o consumo de memória sob envios simultâneos com os limites padrão.

5 aprendizados gerais registrados.
