# Feature 004 — Minhas sessões

- Data: 2026-11-26 · Commit: ver `git log` (Feature: 004-user-sessions) · Agentes: claude

## O que faz
Cada entrada na conta passa a ficar registrada no servidor. Na página `/account/sessions` a pessoa vê seus dispositivos (navegador, sistema operacional, IP parcial e última atividade), encerra um deles ou todos os outros. O efeito vale na próxima requisição, em qualquer instância da API.

## O que mudou
- **API:** `GET /api/identity/account/sessions` (até 50 sessões ativas), `DELETE /api/identity/account/sessions/{id}` e `POST /api/identity/account/sessions/end-others`. Todos usam a política de limite `authenticated`.
- **Registro:** tabela `user_sessions` (migração revisada), com chave gerada no cliente, então entra na auditoria. Sem `xmin`, exceção documentada na ADR 0007.
- **Validação por requisição:** depois da checagem do selo de segurança (ADR 0005), o servidor confere a sessão no cache (Redis, máximo de 60 s). Se faltar o `sid`, a linha, o dono, ou se ela estiver revogada ou expirada, o pedido é recusado. Se o cache e o banco falharem juntos, também é recusado.
- **Todas as entradas** passam por `SessionSignIn`, que grava a linha e o claim `aspireboot:sid`. Troca de papel, remoção de passkey e conclusão de cadastro/recuperação revogam as linhas na mesma transação do selo.
- **Limpeza:** o Worker apaga diariamente (03:17 UTC) as linhas revogadas ou expiradas há mais de 30 dias, em lotes de 1000.
- **Métrica:** `aspireboot.identity.sessions.ended` com os motivos `user`, `others`, `signout` e `stamp`.
- **Site:** página de sessões com diálogo de confirmação para cada encerramento. O dispositivo atual aparece como "Este dispositivo", sem botão de encerrar. Um 401 em qualquer chamada limpa o estado de login e leva à tela de entrada.
- **Cadastro:** se o cookie falhar depois de a conta ser confirmada, a API responde `identity.signInRequired` e o site pede para entrar com a passkey recém-criada.

## Decisões
- **ADR 0007** (registros de sessão no servidor). Principais pontos abaixo.
- **Encerrar a sessão atual retorna 409** (`identity.currentSession`): para isso existe o botão Sair. Id alheio ou inexistente retorna 404, sem revelar de quem é.
- **Fora da auditoria:** o batimento de "última atividade" (uma escrita a cada 5 min por dispositivo, via `ExecuteUpdate`) e a limpeza do Worker. São telemetria e manutenção, e auditá-los encheria a tabela. Criação e revogação continuam auditadas.
- **Batimento invalida o cache, nunca o reescreve**, para não desfazer uma revogação recente.
- **Validade da linha:** igual à do cookie (8 h, deslizante), persistente ou não. Uma sessão sem "manter conectado" continua listada como ativa depois de fechar o navegador, até expirar ou ser encerrada.
- **IP e navegador:** só o IP mascarado (/16 no IPv4, /48 no IPv6) e um resumo `navegador|sistema`. O IP vem dos cabeçalhos encaminhados apenas de `ReverseProxy:KnownNetworks`.

## Configuração
- `UserSessions__LastSeenThrottle` (padrão `00:05:00`, de 1 a 60 min), lido pela API.
- `UserSessions__Retention` (padrão `30.00:00:00`, de 1 a 3650 dias), lido só pelo Worker.
- Ambos estão documentados em `deploy/.env.example`.

## Aviso de atualização
Na primeira versão com esta feature, todos saem da conta uma vez, porque os cookies antigos não têm `sid`. É só entrar de novo com a passkey. Nenhum dado se perde. O aviso fica no README, na seção de deploy.

## Testes
- Unitários: domínio da sessão (revogar, expirar, ativa), mascaramento de IP, resumo do navegador, validador (falha segura, cache, ordem do batimento) e métricas.
- Integração, com PostgreSQL, Redis, RabbitMQ e Mailpit reais: ciclo da sessão (entrar, sair, repetir cookie antigo), revogação entre duas instâncias, os três endpoints (404, 409, 204 repetido), revogações por troca de papel, remoção de passkey e recuperação com auditoria, falha do cookie no cadastro (`identity.signInRequired`), cabeçalhos encaminhados de rede confiável e de fora, logs sem IP nem navegador, e a limpeza do Worker. A métrica `sessions.ended` é conferida com `MeterListener` nos motivos `signout` e `stamp`.
- Site: o interceptor limpa o estado de login no 401, só o caminho exato `/api/identity/account/me` é isento, e a mensagem `identity.signInRequired` existe nos três idiomas.
- Resultado: `./scripts/verify.sh` termina com código 0, com **498 testes do servidor** e **186 do site** passando (94,6% das linhas no site).

## Notas de segurança
Em linguagem simples:
- Cada dispositivo tem uma "ficha" no servidor. Encerrar a ficha derruba o dispositivo na próxima ação, mesmo que alguém tenha copiado o cookie.
- Cada pessoa só enxerga e encerra as próprias sessões. O servidor filtra pelo dono em todo pedido.
- Guardamos só um pedaço do IP e o tipo de navegador, nunca o IP inteiro nem o texto completo do navegador, e nada disso vai para os logs.
- Se algo falha na checagem, a sessão é recusada, e nunca aceita por engano.

**Riscos residuais:**
- Janela de até 60 s se uma leitura do cache coincidir com a revogação (corrida de repreenchimento, aceita na ADR 0007).
- Se a atualização e a remoção do cache falharem juntas após a gravação, a sessão pode parecer ativa por até 60 s (fica no log como erro).
- Linha órfã se o cookie falhar depois do cadastro: ninguém a usa e a limpeza a apaga.
- Batimento e limpeza não entram na auditoria.
- Com o Redis fora do ar, cada requisição consulta o banco.
- A limpeza assume um único nó agendador. Se não rodar por um dia, a execução seguinte recupera.
- Sessão sem "manter conectado" aparece ativa na lista após fechar o navegador, até expirar.

## Pendências
- Testar à mão, no navegador, a página de sessões em 320px e com leitor de tela.
- Rodar `scripts/verify-images.sh` para as imagens Linux.

3 aprendizados gerais registrados.
