# Monitoramento de PCs — Comprar x Construir

Portoconseg · levantamento de 06/11/2026

> **Como ler os preços:** valores em **dólar (US$)**, por usuário/mês, sem impostos, conforme publicados pelos próprios fabricantes. Os sites oficiais de preço estavam bloqueados neste ambiente; os números foram obtidos via busca restrita aos domínios oficiais (páginas de preço, blog e central de ajuda de cada fabricante). Algumas fontes do mesmo fabricante divergem — isso está anotado. **Confirme no site antes de contratar.**

## 1. Resumo — a resposta curta

1. **Nenhuma ferramenta pronta entrega os 4 opcionais que você escolheu** (justificativa de ausência, USB, captura de tela, inventário) num único produto barato. Combinações verificadas:
   - **Monitoo (brasileira):** ociosidade + USB + inventário, **sem captura de tela**. A mais barata. PT-BR.
   - **DeskTime:** ociosidade + **justificativa de ausência ("offline time")** + captura de tela com desfoque. Sem USB encontrado.
   - **Teramind:** cobre USB, captura de tela e muito mais — mas é a mais cara e tem modo oculto, que é exatamente o que gera risco jurídico.
2. **Recomendação:** testar **Monitoo (7 dias grátis)** e **DeskTime (14 dias grátis)** em 2–3 máquinas, ao mesmo tempo, antes de gastar um real com desenvolvimento. Construir só faz sentido se, depois do teste, o que faltar for decisivo.
3. **O risco jurídico maior não é a ferramenta, é a configuração.** A ANPD analisou exatamente um caso de software que monitora funcionários com webcam e registro de digitação e concluiu que o legítimo interesse **não** se aplica, **mesmo com aviso prévio na política de privacidade** (ver seção 5).

## 2. Comparativo de ferramentas prontas

| Ferramenta | Preço (anual) | Mín. usuários | Teste grátis | Ociosidade | Justificativa de ausência | USB | Captura de tela | Inventário | Modo oculto | PT-BR |
|---|---|---|---|---|---|---|---|---|---|---|
| **Monitoo** 🇧🇷 | US$ 4,99 (até 10) · 3,99 (11–100); **-20% no anual** | 10 (plano Advanced) | 7 dias | ✅ (2 min) | não encontrado | ✅ registra cópias | ❌ declara que **não** captura | ✅ hardware | não encontrado | ✅ |
| **DeskTime** | Pro US$ 6,42 · Premium 9,17 (mensal: 7 / 10) | não informado | 14 dias | ✅ (3 min, ajustável) | ✅ "offline time" com descrição | não encontrado | ✅ opcional, com desfoque, desligada por padrão | não encontrado | não encontrado | não confirmado |
| **Insightful** | US$ 8 (Workforce Analytics) · 12 · 16 (combo) — fontes divergem (6,40 a 10) | sem mínimo | 7 dias | ✅ | não encontrado | não encontrado | ✅ **desfocada por padrão** | não encontrado | ✅ existe "stealth" | não confirmado |
| **Hubstaff** | Starter US$ 4,99 · Grow 7,50 · Team 10 (mensal: 7 / 9 / 12) | 2 | 14 dias | ✅ (5/10/20 min) | parcial (manter/descartar tempo ocioso) | não encontrado | ✅ com desfoque | não encontrado | não encontrado | não confirmado |
| **ActivTrak** | US$ 10 · 15 · 17 + **captura de tela é add-on de US$ 2** | 5 (doc. antiga) | plano grátis até 3 usuários (doc. antiga) | ✅ | não encontrado | ✅ alarme ao conectar/gravar USB | ✅ só com add-on, sem desfoque documentado | não encontrado | agente pouco visível (nota de 2020) | não confirmado |
| **Teramind** | Starter ~US$ 14 · UAM ~28 · DLP ~32 (mensal: 15 / 30 / 35) | 5 | não confirmado | ✅ | não encontrado | ✅ **bloqueia** cópia para USB (regras) | ✅ + gravação de vídeo | não encontrado | ✅ agente "Stealth" | não confirmado |

"Não encontrado" = não achei em fonte oficial. **Não significa que não existe** — pergunte ao fornecedor.

### Custo em 12 meses — 10 máquinas (plano anual, sem impostos, sem câmbio)

| Ferramenta / plano | Conta | Total 12 meses |
|---|---|---|
| Monitoo Advanced (com -20% anual) | 4,99 × 0,8 × 10 × 12 | **US$ 479** |
| DeskTime Pro | 6,42 × 10 × 12 | **US$ 770** |
| Insightful Workforce Analytics | 8 × 10 × 12 | **US$ 960** |
| DeskTime Premium | 9,17 × 10 × 12 | **US$ 1.100** |
| Hubstaff Team | 10 × 10 × 12 | **US$ 1.200** |
| ActivTrak básico + captura de tela | (10 + 2) × 10 × 12 | **US$ 1.440** |
| Teramind Starter | 14 × 10 × 12 | **US$ 1.680** |

Para 5 ou 20 máquinas, divida ou multiplique por 2 (exceto Monitoo, que cobra mínimo de 10). Não converti para reais de propósito: o câmbio muda e cartão internacional tem IOF — faça a conta no dia.

## 3. Construir o próprio sistema — custo real

O que **sai barato**: licença (zero), hospedagem (você já tem a Hostinger), código (eu escrevo).

O que **não sai barato**, e é aqui que a maioria subestima:

- **Teste em Windows real:** este ambiente é Linux. Cada versão precisa ser instalada e testada por alguém numa máquina da corretora.
- **Antivírus:** um programa que lê janela ativa, USB e tira print se comporta como malware. Sem **certificado de assinatura de código** (custo anual pago a uma autoridade certificadora), o Windows Defender e outros antivírus tendem a bloquear ou alertar.
- **Manutenção contínua:** atualizações do Windows quebram coisas; alguém precisa corrigir.
- **Suporte:** quando o agente parar numa máquina, quem vai lá ver?
- **Responsabilidade LGPD:** você vira o fornecedor *e* o controlador. Se vazar print de tela com CPF de cliente da sua própria hospedagem, a responsabilidade é integralmente da corretora.

**Vantagem real de construir:** cobre os 4 opcionais juntos, dados ficam na sua hospedagem (no Brasil, se o servidor da Hostinger for no Brasil — confirmar), regras sob medida (ex.: classificar o sistema da Porto como "produtivo").

## 4. A hospedagem Hostinger serve?

Depende do plano — **me diga qual é o seu**.

| Seu plano | Serve para o painel? | Observação |
|---|---|---|
| Single / Premium (compartilhada) | ✅ com **PHP + MySQL** | Single não tem SSH. Atenção ao limite de disco se ativar captura de tela. |
| Business / Cloud | ✅ PHP+MySQL ou Node.js | Node.js aparece a partir do Business. |
| VPS | ✅ qualquer tecnologia | Mais trabalho de administração. |

Fontes: tutorial oficial Hostinger e análises de terceiros — **confirme no painel da Hostinger**. Para ferramentas prontas, a Hostinger **não é usada**: o painel fica no servidor do fabricante.

## 5. LGPD — o que o guia oficial da ANPD diz

O *Guia Orientativo — Hipóteses legais de tratamento de dados pessoais: Legítimo Interesse* (ANPD) analisa o caso de uma empresa que usa software que registra atividades do funcionário, inclusive por webcam e pelo que é digitado. Conclusões do guia:

- a coleta vai além do necessário;
- "no contexto da relação de emprego, os empregados estão em posição de maior vulnerabilidade em face de seu empregador, não possuindo meios efetivos de oposição ao tratamento";
- por isso, **não é admissível usar legítimo interesse nesse caso**, **mesmo que o monitoramento tenha sido previamente informado** e conste da política de privacidade.

**Tradução prática para a Portoconseg:**

| Pode (com política e termo) | Evite |
|---|---|
| Tempo ativo/ocioso, apps e sites (domínio), logon/logoff | Registro de teclas (keylogger) |
| Registro de USB conectado / arquivos copiados | Webcam e microfone |
| Inventário de hardware | Modo oculto ("stealth") |
| Justificativa de ausência | Captura de tela contínua e sem desfoque |
| Captura de tela **desfocada, esporádica, retenção curta**, só se houver motivo documentado | Usar o "tempo ocioso" sozinho para punir ou demitir |

**Correção sobre o que eu disse antes:** no plano, citei um número de processo do TST sobre e-mail corporativo. **Não consegui confirmá-lo em fonte primária** e por isso ele foi retirado. O entendimento geral (monitorar ferramenta corporativa é lícito dentro do poder diretivo, com limite na intimidade) aparece em artigos jurídicos — fontes abaixo —, mas para uso formal peça ao seu advogado a jurisprudência atualizada.

## 6. Modelo de termo de ciência (rascunho — revisar com advogado trabalhista)

> **TERMO DE CIÊNCIA — MONITORAMENTO DE RECURSOS DE INFORMÁTICA**
>
> Eu, ____________________, CPF ____________, declaro ciência de que os computadores da **[RAZÃO SOCIAL DA CORRETORA]** são ferramentas de trabalho e que neles está instalado software de monitoramento, **visível na barra de tarefas**, que registra: (i) horário de logon, logoff, bloqueio e desbloqueio; (ii) períodos sem uso de teclado e mouse; (iii) nomes de programas e domínios de sites utilizados; (iv) conexão de dispositivos USB e arquivos copiados para eles; (v) inventário de hardware; e (vi) [se ativado] capturas de tela desfocadas, a cada ___ minutos, mantidas por no máximo 7 dias.
>
> **Não são coletados:** o conteúdo do que é digitado, senhas, imagens de webcam, áudio do microfone.
>
> **Finalidade:** gestão da jornada, segurança da informação e proteção dos dados de clientes. Os dados não serão usados isoladamente para aplicação de penalidades; ausências do computador podem ser justificadas no próprio sistema (ex.: ligação com cliente, visita, reunião).
>
> **Acesso e prazo:** os dados são acessados somente por [cargo/nome do responsável] e mantidos por ___ dias, sendo depois excluídos. Posso consultar meus próprios registros a qualquer momento e solicitar informações pelo e-mail ____________.
>
> Local e data: ______________ Assinatura: ______________

## 7. Próximos passos

1. Responder: **quantas máquinas** e **qual plano Hostinger**.
2. Abrir teste grátis do **Monitoo** e do **DeskTime** em 2–3 máquinas na mesma semana.
3. Perguntar a cada fornecedor, por escrito: onde ficam os dados (país do servidor — art. 33 da LGPD), se têm DPA/contrato de operador, e se o agente pode ficar **visível**.
4. Levar a política + termo ao advogado trabalhista antes de instalar.
5. Só depois decidir se vale construir.

## Fontes

**Ferramentas (domínios oficiais dos fabricantes)**
- Monitoo — preços: https://monitoo.com.br/en/precos/
- Monitoo — ociosidade: https://monitoo.com.br/en/descubra-como-identificar-a-ociosidade-dos-funcionarios-em-tempo-real-com-o-monitoo/
- Monitoo — USB: https://monitoo.com.br/en/como-controlar-e-monitorar-arquivos-copiados-via-usb-por-funcionarios-em-home-office-1/
- Monitoo — funcionalidades (inventário, sem captura de tela): https://monitoo.com.br/es/monitoo-el-mejor-software-gratuito-de-monitoreo-de-empleados/ · https://monitoo.com.br/en/monitoo-o-melhor-software-gratuito-de-monitoramento-de-funcionarios/ · https://monitoo.com.br/en/principais-funcionalidades-de-um-programa-de-monitoramento/
- DeskTime — preços: https://desktime.com/pricing e https://desktime.com/blog/time-tracking-pricing/
- DeskTime — offline time: https://help.desktime.com/hc/en-us/articles/25079786567709-Logging-offline-time
- DeskTime — capturas de tela: https://desktime.com/features/time-tracking-with-screenshots
- Insightful — capturas desfocadas: https://www.insightful.io/screen-monitoring
- Insightful — privacidade/stealth: https://help.insightful.io/en/articles/4035926-employee-privacy-protection
- Insightful — preços (página do fabricante): https://www.insightful.io/blog/faqs-about-insightful
- Hubstaff — preços: https://hubstaff.com/blog/hubstaff-pricing-plan-comparison/
- Hubstaff — ociosidade: https://hubstaff.com/idle-time-tracking-software
- Hubstaff — desfoque: https://hubstaff.com/productivity-monitoring/privacy-first-employee-monitoring
- ActivTrak — preços: https://www.activtrak.com/pricing/
- ActivTrak — add-on de tela: https://support.activtrak.com/hc/en-us/articles/11780652431771-Screen-Details-Add-on
- ActivTrak — alarmes (USB): https://support.activtrak.com/hc/en-us/articles/360030844592-ActivTrak-Alarms-Overview
- ActivTrak — planos: https://support.activtrak.com/hc/en-us/articles/360027324491-ActivTrak-Plans-and-Support-Overview
- Teramind — preços (blog do fabricante): https://www.teramind.co/blog/time-doctor-vs-teramind/ · https://www.teramind.co/blog/hubstaff-vs-teramind/ · https://www.teramind.co/blog/empmonitor/
- Teramind — guia de regras (USB): https://www.teramind.co/wp-content/uploads/Teramind-Rules-Guide.pdf
- Teramind — agente oculto/visível: https://knowledge.teramind.co/en/articles/13335212-deploying-using-the-teramind-agent

**Hostinger**
- https://www.hostinger.com/pt/tutoriais/como-escolher-plano-de-hospedagem
- https://horadecodar.com.br/vps-hostinger-vs-hospedagem-compartilhada-2026/

**Jurídico**
- ANPD — Guia Legítimo Interesse: https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/copy_of_guia_legitimo_interesse.pdf/@@display-file/file
- ANPD — notícia do lançamento: https://www.gov.br/anpd/pt-br/assuntos/noticias/anpd-lanca-guia-orientativo-sobre-legitimo-interesse
- LGPD (Lei 13.709/2018): https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm
- Conjur — monitoramento e Justiça do Trabalho: https://www.conjur.com.br/2022-out-11/rony-vainzof-data-loss-prevention-justica-trabalho2/
- OAB-DF — LGPD nas Relações de Trabalho: https://oabdf.org.br/wp-content/uploads/2021/08/eBook_LGPD-nas-Relacoes-de-Trabalho-1-1.pdf
- Ambito Jurídico — software de produtividade e privacidade: https://ambitojuridico.com.br/software-que-mede-produtividade-pode-violar-a-privacidade/
