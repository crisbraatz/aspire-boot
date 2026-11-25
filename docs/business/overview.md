# AspireBoot — Visão de Negócio

## Visão
Um template .NET Aspire para que pessoas de tecnologia ou leigas tirem ideias do papel com segurança. O template já vem com a infraestrutura e a autenticação prontas, para que o foco fique no negócio, e não em recriar o básico de toda aplicação com login.

## Problema e público
Todo produto web com login exige o mesmo trabalho repetitivo: cadastro, login, recuperação de conta, sessões, permissões, e-mail, upload de arquivos, HTTPS, banco, cache e filas. Fazer isso de forma segura é difícil e demorado, principalmente para quem não é desenvolvedor ou trabalha com assistentes de IA. Público: pessoas leigas e iniciantes que constroem com IA, desenvolvedores experientes que querem começar rápido e o próprio autor, com seus projetos e clientes.

## Proposta de valor
Começar já seguro e funcionando: clonar, rodar e ter um app completo com contas, permissões (RBAC) e infraestrutura profissional. A documentação é simples o bastante para pessoas leigas.

## Modelo de negócio
Código aberto e gratuito (licença MIT) para a comunidade. Também serve de base para os projetos pessoais e de clientes do autor. Sem pagamentos embutidos.

## Usuários e papéis
- **Usuário comum:** cria conta, faz login, gerencia o próprio perfil, a conta e as sessões.
- **Administrador:** além disso, gerencia usuários conforme as permissões (RBAC).
- **Quem usa o template:** a pessoa que clona e constrói o próprio produto em cima dele.

## Canais e plataformas
API mais um frontend web pronto. Um app mobile está previsto para o futuro (a API deve servir a ambos). Interfaces em português, inglês e espanhol. Todo o código e a documentação técnica ficam em inglês.

## Funcionalidades presentes desde o início
- Página inicial pública e página "não encontrada".
- Contas sem senha: cadastro com confirmação de e-mail (com reenvio), registro de passkeys, login com passkey (com opção de manter conectado), logout, recuperação de conta por e-mail e gestão das passkeys (listar, adicionar, renomear e remover; a última passkey nunca pode ser removida).
- Perfil: ver e editar o próprio perfil, incluindo foto (upload), e as preferências (idioma, tema).
- Usuários: listagem, detalhe de um usuário, edição e exclusão conforme o papel.
- Sessões: ver as sessões ativas, encerrar uma sessão específica, encerrar todas as outras e sair.
- RBAC: papéis e permissões controlando o acesso às telas e aos endpoints.
- Infraestrutura: banco de dados, mensageria, cache, armazenamento de arquivos, envio de e-mail, HTTPS, observabilidade (logs, métricas, traces) e health checks.
- Trabalho em segundo plano: envio assíncrono de e-mails, auditoria e limpeza periódica de arquivos órfãos.

## Premissa: desenvolvimento assistido por IA com qualidade
O template traz instruções e skills de IA fortes, específicas das tecnologias e da arquitetura adotadas. Assim, os assistentes de IA evoluem o produto sem se desviar da arquitetura e sem espalhar código fora do lugar. Todo código gerado deve ser fácil de manter, bem organizado, performático, seguro e testado, seguindo convenções claras sobre onde cada coisa vive.

## Dados e conformidade
Guarda dados pessoais básicos (nome, e-mail, foto). Por enquanto não segue uma lei específica (LGPD/GDPR), apenas boas práticas gerais de segurança: contas sem senha (a entrada é só por passkey), cookies seguros, proteção CSRF, limites de requisições por usuário e por IP, e segredos fora do código.

## Integrações
Um serviço SMTP externo para e-mail. As demais peças (banco, fila, cache, armazenamento) rodam em containers próprios. Não há gateway de pagamento.

## Restrições
- O deploy padrão é em uma VPS barata, com containers. Nuvem gerenciada (Azure/AWS) fica como opção.
- Desenvolvimento por uma pessoa, apoiada por IA (suposição).
- Sem prazo fixo (suposição).

## Métricas de sucesso
Primeira versão pronta quando uma pessoa clona o repositório, roda um comando e em poucos minutos tem funcionando, com HTTPS: cadastro, login com passkey, confirmação por e-mail, recuperação de conta, upload de foto, listagem e detalhe de usuários, edição das próprias configurações e RBAC. Depois, ela publica na VPS seguindo um guia simples.

## Suposições e perguntas em aberto
- Equipe de uma pessoa com IA e sem prazo fixo (suposição).
- Papéis iniciais: Admin e User (suposição).
- App mobile e conformidade LGPD/GDPR ficam para depois.
