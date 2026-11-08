# Histórico de versões do Monitor Portoconseg

Cada versão tem um número no formato **maior.menor.correção** (ex.: 1.0.0):

- **Correção** (1.0.**1**): conserta um problema, sem mudar o que o sistema faz.
- **Menor** (1.**1**.0): acrescenta um recurso novo, sem mudar o que já existia.
- **Maior** (**2**.0.0): muda algo que já existia, de um jeito que exige atenção (ex.: reinstalar o programa nos computadores).

O número fica no arquivo `VERSION`, na raiz do repositório, e aparece em quatro lugares:

| Onde | Como aparece |
|---|---|
| Arquivo do programa | `MonitorPortoconseg-1.0.0.exe` e `MonitorPortoconseg-1.0.0-instalador.zip` |
| Ícone ao lado do relógio | Passar o mouse ou clicar com o botão direito: "Monitor Portoconseg 1.0.0" |
| Painel | No topo, ao lado do nome, e na coluna Versão da tela Computadores |
| Propriedades do arquivo no Windows | Botão direito no .exe → Propriedades → Detalhes → Versão do produto |

No computador o programa é instalado sempre como `C:\Program Files\MonitorPortoconseg\MonitorPortoconseg.exe`, sem o número no nome, para que cada atualização substitua a anterior. A versão continua visível no ícone, no painel e nas propriedades do arquivo.

---

## 1.0.0 — 08/11/2026

Primeira versão (entrega 1).

**Programa nos computadores (Windows 10 e 11)**
- Inicia sozinho ao entrar no Windows; ícone azul visível ao lado do relógio; não faz perguntas ao funcionário.
- A cada 5 segundos registra: ativo, inativo (5 minutos ou mais sem teclado e mouse) ou tela bloqueada; programa e título da janela em uso; nos navegadores, o site aberto.
- Uso pessoal (bancos, redes sociais, vídeo e música, compras, jogos, Airbnb e afins) sai do computador só como tempo, sem nome do site nem título.
- Sem internet, guarda os dados no próprio computador e envia quando a conexão volta.
- Não registra o que é digitado, não tira fotos da tela, não grava vídeo e não lê conversas.

**Painel (André e Renata)**
- Dia: chegada, saída, ativo, inativo, fora do computador, uso pessoal, ativo fora do horário, linha do tempo, lista do que foi feito e registro detalhado janela a janela.
- Semana: fechamento por dia e total; na segunda-feira abre na semana que terminou.
- Classificar: marca sites e programas como trabalho, pessoal ou neutro com um clique.
- Computadores: cadastro com chave por máquina e download do instalador.
- Horários: horário combinado editável, valendo a partir de uma data, sem alterar relatórios anteriores. Inicial: seg a qui 8h–18h, sex 8h–17h.
- Acessos: quem entrou no painel e o que consultou ou alterou.

**Servidor (VPS em Campinas)**
- HTTPS automático, login com senha, chave por computador.
- Registros apagados após 90 dias; cópia diária do banco (14 mais recentes).

**Não testado ainda nesta versão:** programa rodando em Windows real e montagem do servidor no VPS. Serão conferidos no piloto.
