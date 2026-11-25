---
name: domain
description: Load before planning or implementing any feature to use the AspireBoot business vocabulary, rules and workflows (accounts, profiles, sessions, users, roles).
level: 3
updated: 2026-11-25
---

# Domínio do AspireBoot

## Glossário
- **Template**: o próprio AspireBoot, a base que alguém clona para criar o seu produto.
- **Construtor(a)**: a pessoa (técnica ou leiga) que usa o template para tirar uma ideia do papel.
- **Usuário**: pessoa com conta no app construído sobre o template.
- **Conta**: credenciais do usuário (e-mail e senha), confirmação de e-mail e bloqueio por tentativas.
- **Perfil**: dados públicos do usuário dentro do app (nome de exibição, foto, preferências de idioma e tema).
- **Nome de exibição**: como o usuário aparece para os outros.
- **Sessão**: um dispositivo ou navegador conectado à conta.
- **Papel (role)**: conjunto de permissões. Os papéis iniciais são **Admin** e **User**.
- **Permissão**: o que um papel pode fazer (por exemplo, ver ou gerenciar usuários).

## Regras de negócio
1. Só faz login quem confirmou o e-mail. (fonte: entrevista)
2. O e-mail é único por conta. (fonte: entrevista)
3. Login, recuperação de senha e cadastro nunca revelam se um e-mail já existe. (fonte: entrevista, segurança)
4. Depois de várias tentativas de login erradas, a conta fica bloqueada temporariamente. (fonte: entrevista)
5. Trocar e-mail ou senha exige a senha atual, e o novo e-mail precisa ser confirmado. (fonte: entrevista)
6. O usuário edita o próprio perfil e as próprias preferências. Mexer em outros usuários depende do papel. (fonte: entrevista)
7. O usuário vê as próprias sessões ativas e pode encerrar uma específica, todas as outras ou a atual. (fonte: entrevista)
8. Todo usuário novo recebe o papel **User**. **Admin** é concedido explicitamente. (fonte: suposição)
9. Um Admin não pode remover o próprio papel de Admin. (fonte: suposição)
10. As telas existem em português, inglês e espanhol. O usuário escolhe o idioma. (fonte: entrevista)
11. O nome de exibição tem de 2 a 100 caracteres, sem caracteres de controle. (fonte: scaffold)

## Invariantes
- Toda conta tem um e-mail único e um nome de exibição válido.
- Permissões são sempre verificadas no servidor. A interface só esconde o que não é permitido.
- Senhas, tokens e dados pessoais nunca aparecem em logs, métricas ou URLs.

## Fluxos
### Cadastro
1. A pessoa informa nome de exibição, e-mail e senha.
2. O sistema cria a conta (não confirmada) e envia um e-mail de confirmação em segundo plano.
3. A pessoa clica no link, o e-mail é confirmado e o login fica liberado.

### Recuperação de senha
1. A pessoa informa o e-mail. A resposta é sempre a mesma, exista a conta ou não.
2. Se a conta existe, chega um link com validade curta.
3. A pessoa define a nova senha. As outras sessões são encerradas.

### Gestão de usuários (conforme o papel)
1. Listar usuários (com paginação e busca).
2. Ver o detalhe de um usuário.
3. Editar ou excluir, se o papel permitir.
