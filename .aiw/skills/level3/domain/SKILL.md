---
name: domain
description: Load before planning or implementing any feature to use the AspireBoot business vocabulary, rules and workflows (contas sem senha com passkeys, cadastro, recuperação, sessões e encerramento de sessões, perfil e avatar, papéis).
level: 3
updated: 2026-11-26
---

# Domínio do AspireBoot

## Glossário
- **Template**: o próprio AspireBoot, a base que alguém clona para criar o seu produto.
- **Construtor(a)**: a pessoa (técnica ou leiga) que usa o template para tirar uma ideia do papel.
- **Usuário**: pessoa com conta no app construído sobre o template.
- **Conta**: identidade do usuário no app. Tem um e-mail confirmado, uma ou mais passkeys e um perfil. Não existe senha.
- **Passkey**: credencial criada no dispositivo ou no gerenciador de senhas da própria pessoa (WebAuthn). É o único jeito de entrar na conta. Cada conta tem uma ou mais passkeys, cada uma com nome próprio (por exemplo, "Notebook do trabalho").
- **Cadastro**: criação da conta a partir do e-mail, seguida da confirmação do e-mail e do registro da primeira passkey.
- **Confirmação de e-mail**: clique no link enviado ao e-mail. Confirma o endereço e libera o registro da primeira passkey.
- **Recuperação de conta**: caminho para registrar uma nova passkey quando a pessoa perdeu o acesso às suas passkeys. Passa pelo e-mail.
- **Link de e-mail**: link com um token de uso único que confirma o e-mail ou inicia a recuperação. Vale por 2 horas.
- **Intervalo de reenvio**: tempo mínimo de 60 segundos entre dois e-mails de confirmação ou recuperação para a mesma conta.
- **Sessão**: um dispositivo ou navegador conectado à conta. Cada entrada na conta abre uma sessão, que dura 8 horas e se renova com o uso.
- **Encerrar sessão**: desconectar um dispositivo específico da conta. A sessão encerrada deixa de valer na próxima ação naquele dispositivo.
- **Este dispositivo**: a sessão que a pessoa está usando agora. Ela não pode ser encerrada pela lista de sessões; para sair, usa-se "Sair".
- **Sair**: desconectar o dispositivo atual. Encerra a sessão atual e a deixa de valer.
- **Última atividade**: momento em que a sessão foi usada pela última vez. É atualizado no máximo a cada 5 minutos.
- **Manter conectado**: escolha explícita, no login, de manter a sessão salva no navegador.
- **Nome de exibição**: como o usuário aparece para os outros.
- **Perfil**: dados do usuário dentro do app (nome de exibição, avatar e preferências de idioma e tema).
- **Avatar**: foto de perfil do usuário. Aceita JPEG, PNG ou WebP, de até 5 MB. O app guarda sempre uma versão quadrada de 256x256 em WebP, sem metadados como a localização da foto.
- **Referência do avatar**: o ponteiro para o avatar atual da conta. Tem uma versão, que só aumenta, e nunca é escolhido pelo usuário.
- **Versão do avatar**: número que muda a cada troca de avatar. Faz a imagem antiga deixar de valer, mesmo que alguém tenha o endereço dela.
- **Idioma preferido**: idioma da interface e dos e-mails da conta (pt-BR, en-US ou es-ES).
- **Papel (role)**: conjunto de permissões. Os papéis iniciais são **Admin** e **User**.
- **Permissão**: o que um papel pode fazer (por exemplo, ver ou gerenciar usuários).

## Regras de negócio
1. Não existe senha em nenhum ponto do produto: não há cadastro, login, troca ou recuperação por senha. A entrada na conta é sempre por passkey. (fonte: feature 001)
2. Só entra quem confirmou o e-mail. Um cadastro sem confirmação não tem acesso ao app. (fonte: entrevista, feature 001)
3. Depois de confirmar o e-mail, a pessoa registra a primeira passkey. Sem passkey, a conta não entra. (fonte: feature 001)
4. Uma conta nunca fica sem passkey. Não é possível remover a última passkey, e a regra vale mesmo com dois pedidos de remoção ao mesmo tempo. (fonte: feature 001)
5. O e-mail é único por conta. (fonte: entrevista)
6. Cadastro, reenvio de confirmação e recuperação respondem da mesma forma, exista a conta ou não. A pessoa nunca descobre pela resposta se um e-mail já tem conta. (fonte: entrevista, segurança)
7. Os links de confirmação e de recuperação valem 2 horas e são de uso único. Usar o link encerra a possibilidade de usá-lo de novo. (fonte: feature 001)
8. Recuperação só é oferecida para contas com e-mail confirmado. Ao concluir a recuperação, todas as sessões anteriores são encerradas e a pessoa registra uma nova passkey. (fonte: feature 001)
9. Remover uma passkey encerra as outras sessões da conta. (fonte: feature 001)
10. E-mails de confirmação e recuperação têm intervalo de 60 segundos por conta. Um pedido dentro desse intervalo não envia outro e-mail. (fonte: feature 001)
11. Os e-mails da conta usam o idioma preferido da pessoa. O idioma escolhido na tela de entrada vale para o cadastro e é salvo na conta. (fonte: feature 001)
12. O usuário edita o próprio perfil e as próprias preferências. Mexer em outros usuários depende do papel. (fonte: entrevista)
13. O usuário vê as próprias sessões ativas e pode encerrar uma específica ou todas as outras. Para encerrar a sessão atual, a pessoa usa "Sair". (fonte: entrevista, feature 004)
14. Todo usuário novo recebe o papel **User**. **Admin** nunca é concedido por pedido de recuperação. Só o e-mail configurado no bootstrap vira Admin, na confirmação do próprio e-mail, uma única vez. (fonte: feature 003)
15. Um Admin não pode remover o próprio papel de Admin. (fonte: suposição)
25. Cada pessoa tem um único papel: **User** ou **Admin**. Promover é trocar User por Admin, e rebaixar é o inverso. (fonte: feature 003)
26. Sempre existe pelo menos um Admin. O último Admin não pode ser rebaixado, mesmo com dois pedidos ao mesmo tempo. (fonte: feature 003)
27. Mudar o papel de alguém encerra as sessões dessa pessoa. O novo papel vale no próximo login. (fonte: feature 003)
28. Só o Admin lista, vê o detalhe e muda o papel de outros usuários. Nenhuma pessoa comum vê dados de outros. (fonte: feature 003)
29. Toda alteração de dados da conta e do papel fica registrada na auditoria, com quem fez e quando. O registro não pode ser editado nem apagado pela aplicação. (fonte: feature 003)
30. Toda sessão mostra o navegador, o sistema operacional, o endereço IP parcial (dois primeiros blocos do IPv4 ou os 48 primeiros bits do IPv6), a data de entrada e a última atividade. O endereço IP completo e o texto completo do navegador nunca são guardados. (fonte: feature 004)
31. Encerrar uma sessão a torna inválida na próxima ação naquele dispositivo, mesmo que o cookie ainda esteja no navegador. (fonte: feature 004)
32. Encerrar uma sessão de outra pessoa, ou uma que não existe, responde como se a sessão não existisse. Encerrar uma sessão já encerrada não causa erro. (fonte: feature 004)
33. Remover uma passkey encerra as outras sessões e mantém a sessão de quem fez a remoção. Mudar o papel encerra todas as sessões da pessoa. Concluir uma recuperação encerra as sessões anteriores. As sessões afetadas deixam de valer na hora. (fonte: feature 004)
34. Sessões encerradas ou expiradas são apagadas automaticamente depois de 30 dias. Elas não aparecem na lista de sessões ativas. (fonte: feature 004)
16. O nome de exibição tem de 2 a 100 caracteres, sem caracteres de controle. (fonte: scaffold)
17. As telas existem em português, inglês e espanhol. O usuário escolhe o idioma, e a escolha é salva na conta quando ela está conectada. (fonte: entrevista, feature 001)
18. O avatar aceita apenas JPEG, PNG ou WebP, de até 5 MB, com no máximo 8000 px por lado e 24 milhões de pixels. Um arquivo fora desses formatos é recusado, mesmo com extensão de imagem. (fonte: feature 002)
19. O app guarda o avatar como imagem quadrada de 256x256 em WebP. Dados de localização (GPS) e outros metadados da foto não são guardados. (fonte: feature 002)
20. Cada avatar novo tem uma versão maior que a anterior. A versão não volta atrás, mesmo depois de remover o avatar. (fonte: feature 002)
21. Remover o avatar é idempotente: sem avatar, a remoção não muda nada e a resposta é a mesma. (fonte: feature 002)
22. Só o próprio usuário acessa o avatar dele. A imagem não tem endereço público. (fonte: feature 002)
23. Ao trocar ou remover o avatar, a imagem anterior é apagada em segundo plano. Se o envio falhar, o perfil continua com o avatar anterior. (fonte: feature 002)
24. Cada usuário pode enviar até 10 avatares a cada 10 minutos. (fonte: feature 002)

## Invariantes
- Toda conta tem um e-mail único, um nome de exibição válido e um idioma preferido entre os suportados.
- Toda conta tem pelo menos uma passkey.
- Um avatar, quando existe, é sempre uma imagem da própria conta, guardada em versão quadrada de 256x256 sem metadados.
- A versão do avatar só aumenta.
- Nenhum token de confirmação ou recuperação é usado mais de uma vez, nem depois de 2 horas.
- Respostas de cadastro, reenvio e recuperação não revelam se um e-mail existe.
- Permissões são sempre verificadas no servidor. A interface só esconde o que não é permitido.
- Toda conta tem exatamente um papel, e sempre há pelo menos um Admin.
- Um papel alterado não vale em sessões antigas: elas são encerradas na próxima requisição.
- Passkeys, tokens e dados pessoais nunca aparecem em logs, métricas ou URLs.

- Sessão ativa: uma sessão não encerrada, não expirada e que pertence à conta.

## Fluxos
### Cadastro
1. A pessoa informa o nome de exibição e o e-mail, e escolhe o idioma.
2. O sistema cria a conta sem confirmação e envia, em segundo plano, o e-mail de confirmação. A resposta é a mesma, exista o e-mail ou não.
3. A pessoa abre o link do e-mail e confirma o endereço.
4. Na mesma tela, a pessoa cria a primeira passkey no dispositivo dela. A conta fica pronta para entrar e as sessões são abertas.

### Entrada na conta
1. A pessoa escolhe "entrar com passkey". Se o dispositivo oferecer as passkeys salvas, ela escolhe uma. Não é preciso digitar o e-mail.
2. A pessoa confirma com o dispositivo (biometria, PIN ou gerenciador de senhas).
3. Se a pessoa marcou "manter conectado", a sessão continua salva no navegador. Caso contrário, ela termina ao fechar o navegador.
4. Falhas mostram uma mensagem genérica, sem indicar se a passkey ou a conta existem.

### Sessões
1. A pessoa abre a página de sessões a partir da sua conta e vê a lista, com "Este dispositivo" marcado.
2. Para encerrar um dispositivo, a pessoa confirma numa caixa própria. A lista é atualizada e a confirmação é anunciada para leitores de tela.
3. Para encerrar todas as outras, a pessoa confirma numa caixa separada. A sessão atual continua aberta.
4. Um dispositivo que teve a sessão encerrada volta para a tela de entrada na próxima ação.

### Recuperação de conta
1. A pessoa informa o e-mail. A resposta é a mesma, exista a conta ou não.
2. Se a conta existe e o e-mail está confirmado, chega um link válido por 2 horas. Se a conta não existe ou não está confirmada, nada é enviado.
3. A pessoa abre o link e registra uma nova passkey no dispositivo atual.
4. Todas as outras sessões da conta são encerradas. O link não pode ser usado de novo.

### Gestão de passkeys
1. Listar as passkeys da conta, com nome e data de criação.
2. Adicionar uma passkey nova (a conta precisa estar autenticada), com nome de 1 a 50 caracteres.
3. Renomear uma passkey.
4. Remover uma passkey, desde que não seja a última. A remoção encerra as outras sessões e avisa a pessoa por e-mail.

### Perfil e avatar
1. A pessoa edita o nome de exibição na página "Minha conta". As regras do nome valem como antes.
2. Para trocar o avatar, a pessoa escolhe uma imagem JPEG, PNG ou WebP de até 5 MB, ou a arrasta para a área indicada.
3. O sistema confere o formato pelo conteúdo do arquivo, descarta os metadados e gera uma imagem de 256x256. Se a imagem for recusada, o avatar atual continua.
4. A imagem nova passa a valer com uma versão nova. A imagem anterior é apagada em segundo plano.
5. Remover o avatar volta a mostrar as iniciais do nome. A remoção pode ser repetida sem erro.

### Bootstrap do Admin
1. O operador configura o e-mail do primeiro Admin (variável `BOOTSTRAP_ADMIN_EMAIL`). Sem configuração, não há Admin automático.
2. Quando esse e-mail confirma a conta, ela vira Admin. Entrar de novo ou recuperar a conta não concede o papel.
3. Se a conta já for Admin, nada muda.

### Gestão de usuários (Admin)
1. Listar usuários, com busca por nome ou e-mail, filtro por papel e paginação.
2. Ver o detalhe de um usuário (passkeys, idioma, papel).
3. Mudar o papel entre Admin e User, com confirmação. Não é possível rebaixar a si mesmo nem o último Admin.
4. A pessoa afetada recebe um e-mail avisando a mudança de papel, no idioma preferido dela.

### Auditoria
1. Cada alteração relevante gera um registro com a pessoa responsável, a data e os valores antes e depois.
2. Senhas, tokens e chaves nunca aparecem no registro. Dados de passkey também não.
3. O registro não é apagado nem editado pela aplicação.
