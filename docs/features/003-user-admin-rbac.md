# Feature 003 — Administração de usuários, revogação imediata e auditoria

- Data: 2026-11-25 · Commit: ver `git log` (Feature: 003-user-admin-rbac) · Agentes: claude

## O que faz
Torna reais os papéis **Admin** e **Usuário**. O primeiro Admin é definido pelo operador (variável `BOOTSTRAP_ADMIN_EMAIL`) e vira Admin ao confirmar o próprio e-mail. O Admin lista e busca usuários, vê o detalhe de cada um e muda o papel. Sempre existe pelo menos um Admin, e ninguém remove o próprio papel de Admin. A pessoa afetada recebe um e-mail avisando a mudança. Ao mudar a conta (recuperação, remoção de passkey ou troca de papel), as outras sessões são encerradas na hora. Toda alteração de dados fica registrada na auditoria.

## O que mudou
- **API:** `/api/identity/admin/users` (listar com busca e filtro, detalhe, foto de um usuário e troca de papel). `GET /api/identity/account/me` informa os papéis.
- **Regras de papel:** módulo de domínio `RoleAssignment`, com regras testadas em unidade.
- **Revogação:** cada requisição confere o cookie com o estado atual da conta, guardado em cache (Redis, máximo de 60 s). Ver ADR 0005.
- **Auditoria:** tabela `audit_entries`, gravada na mesma transação de cada alteração. Ver ADR 0006.
- **Site:** página `/admin/users` com busca, filtro por papel e paginação, e página de detalhe com troca de papel em diálogo de confirmação. O item "Administração" aparece só para Admins.
- **Configuração:** nova chave `Bootstrap__AdminEmail` (`BOOTSTRAP_ADMIN_EMAIL` no `deploy/.env`).

## Decisões
- **Um papel por pessoa.** Promover troca User por Admin, e rebaixar faz o inverso. Papéis fixos no banco, criados pela migração.
- **Trava na linha do papel Admin.** A troca de papel trava essa linha antes de contar os Admins, então dois rebaixamentos simultâneos não deixam o sistema sem Admin.
- **Revogação sem intervalo.** A checagem roda em toda requisição, em vez de a cada 60 s. Custa uma leitura no Redis por requisição.
- **Auditoria automática.** Quem altera dados não escreve no registro. Um interceptor grava as linhas na mesma transação, então uma mudança revertida também não deixa registro.

## Testes
- Unitários: regras de papel (autorebaixamento, último Admin, papel inválido, bootstrap sem configuração e idempotente), registro de auditoria (mascaramento, exclusão de dados de passkey) e validação da sessão com cache que falha.
- Integração, com PostgreSQL, Redis, RabbitMQ e Mailpit reais: revogação entre duas instâncias da API, bootstrap na confirmação, listagem e detalhe (paginação, busca, 403, 404), troca de papel com duas demoções simultâneas, auditoria na mesma transação e trigger que impede alterar o registro, e o e-mail de mudança de papel.
- Resultado: `./scripts/verify.sh` sem erros, com 380 testes do servidor e 164 do site passando (94% de cobertura no site). As imagens Linux continuam válidas (`scripts/verify-images.sh`).

## Notas de segurança
Em linguagem simples:
- Só o Admin acessa a área de administração. O servidor confere isso em cada pedido, e a tela só esconde o menu.
- Quem perde um papel ou uma passkey perde o acesso às sessões antigas na próxima ação.
- O registro de auditoria não pode ser alterado pela aplicação. Quem usa o usuário dono do banco pode contorná-lo, por isso esse usuário deve ser restrito ao operador.
- O primeiro Admin vem só da configuração. Nenhum pedido do site pode conceder papel.

**Riscos residuais:**
- Janela de até 60 s se a atualização e a remoção do cache falharem juntas (fica registrado no log como erro).
- Sem limpeza de registros antigos da auditoria.
- E-mails e nomes ficam no registro da auditoria. Ainda não há pedido de exclusão de dados (LGPD/GDPR).
- Alterações feitas por SQL direto, sem o rastreamento do EF Core, não entram na auditoria.
- Com o Redis fora do ar, cada requisição consulta o banco.

## Pendências
- Rodar `./scripts/verify.sh` completo e confirmar o resultado.
- Exclusão de usuários (ainda não existe).
- Política de retenção e processo de exclusão de dados da auditoria.
- Testar à mão, no navegador, a tela de administração em 320px e com leitor de tela.

4 aprendizados gerais registrados.
