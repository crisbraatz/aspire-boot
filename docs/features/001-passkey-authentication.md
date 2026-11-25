# Feature 001 — Autenticação sem senha com passkeys

- Data: 2026-11-25 · Commit: ver `git log` (Feature: 001-passkey-authentication) · Agentes: claude

## O que faz
Contas completas **sem nenhuma senha**. A pessoa se cadastra com nome e e-mail, confirma pelo link recebido e cria a primeira passkey na hora. Uma passkey é uma chave guardada no celular, no computador ou no gerenciador de senhas, e é desbloqueada com digital, rosto ou PIN. Depois disso, entra com um clique em "Entrar com passkey", com a opção "manter conectado". Na área logada, a pessoa lista, adiciona, renomeia e remove passkeys, mas nunca a última. Se perder todas, recupera o acesso por e-mail com um link que vale 2 horas e funciona uma vez só. O site troca entre português, inglês e espanhol na hora, sem recarregar, e a escolha fica salva na conta e define o idioma dos e-mails.

## O que mudou
- **API:** fluxos de cadastro, reenvio do link, confirmação com primeira passkey, login, logout, "quem sou eu", gerenciamento de passkeys, recuperação e preferência de idioma.
- **Worker:** e-mails nos 3 idiomas (confirmação, recuperação, passkey adicionada, passkey removida), enviados pela fila.
- **Site:** telas de cadastro, confirmação, login, recuperação, área logada e passkeys, além do seletor de idioma.
- **Banco:** tabela de passkeys e idioma preferido no usuário.

## Decisões
- **Sem senha por design** (ADR 0003): um teste falha se alguém usar senha no código.
- **Limite de tentativas é regra da arquitetura** (ADR 0002): todo endpoint tem um limite, e um teste garante isso.
- **Idiomas trocados na hora** (ADR 0001): solução própria, sem biblioteca nova.
- A decisão inicial de stack foi movida para o ADR 0000.

## Testes
- 144 testes do servidor, contra PostgreSQL, RabbitMQ, Redis e Mailpit reais, com um autenticador de passkey simulado. Incluem a jornada completa, do cadastro à recuperação.
- 96 testes do site, com 92% de cobertura.
- Build, lint, formatação e migrações conferidos com `./scripts/verify.sh`.

## Notas de segurança
- Ninguém descobre se um e-mail tem conta: as respostas são sempre iguais.
- Os links valem 2 horas e uma única vez. A recuperação encerra as outras sessões em até 60 segundos.
- Há proteção contra requisições falsificadas (CSRF) em todos os envios, e as requisições têm limite de tamanho.
- O limite de tentativas não pode ser burlado fingindo outro endereço.
- Os logs não guardam e-mails nem códigos.
- **Riscos aceitos:**
  - o tempo de resposta do cadastro pode indicar se o e-mail existe, o que o limite de tentativas reduz;
  - o intervalo entre reenvios não é atômico;
  - a data de último uso da passkey não existe no .NET 10.

## Pendências
- Testar à mão no navegador: tela de 320px, zoom de 200% e verificador de acessibilidade.
- Subir a stack de produção (compose) de ponta a ponta.
- Aviso na tela quando salvar o idioma na conta falhar.
- Separar os testes unitários dos containers.

8 aprendizados gerais registrados.
