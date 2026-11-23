# Auditoria do porte NativeServer para o build 3663 (r22)

Data: 14/11/2026. Ferramentas no servidor: `audit/audit_offsets.py` e `audit/audit2.py` (cada constante do código
original que cita um endereço do build 2976 tem a instrução citada alinhada com a do 3663 e o deslocamento comparado).

## Método
- 680 constantes com endereço citado. 125 confirmadas iguais na primeira passada, 155 na varredura da função inteira.
- 17 + 30 apontaram mudança; cada uma foi conferida instrução por instrução. Falsos positivos descartados:
  `kSubstituteBlock` e `kSeqComp_Mgr` (pilha / prólogo), `kHolds` e `kHoldMaxUs` (números decimais, não offsets),
  `kActor_ChildCount` e `kActor_Local` (a instrução casada era de outra estrutura).
- As correções que eu já tinha feito à mão (TLS 0x1EB, Status +0x90, gerenciador +0x38 etc.) saíram iguais na
  auditoria, o que valida o método.

## Corrigido na r22 (ns_manual.py, seções 22 e 23)
| Onde | Antes | 3663 | Evidência |
|---|---|---|---|
| Estrutura P do carregamento do convidado (estado) | +0x15C | +0x24C | 0x7797D0 -> 0x882699 (25 instruções) |
| P: gerenciador / Q / flags / pos | 0xB8 / 0xC0 / 0x160,0x162 / 0x150 | 0x1A8 / 0x1B0 / 0x24F,0x251 / 0x240 | mesma função |
| Gancho de transição de estado | lia `rdi` | `r13` | a função passou a guardar P em r13 |
| Números dos estados de carregamento (de 0xB em diante) | ocioso 0x18, espera 0x12, 0x11 | 0x19, 0x13, 0x12 | gravações `mov [P+estado], N` alinhadas |
| Portão "no mundo" do XVeto | 0x18 | 0x19 | idem |
| Stage B60/B61 | 0x60/0x61 | 0x68/0x69 | 0x602E9C |
| Timeline listed / playing | 0x1D1 / 0xDC | 0x1F1 / 0xFC | 0x34DB973, 0xAA4450 |
| Nó pai / flags do contexto | 0x98 / 0x5C | 0xB8 / 0x64 | 0xB27932, 0xB278B8 |
| Instância -> stage | 0x58 | 0x60 | 0xA9E62E |
| Movimento: flag vazio | 0x3D0 | 0x430 | 0x88E6CC |
| Física vfTransform | 0x3B0 | 0x3B8 | 0x5A5495 |
| Árvores: F6 / quantidade / direção | 0xF6 / 0x378 / 0x380 | 0xFA / 0x3C0 / 0x3C8 | 0x117F873, 0x119127B |
| Raio de spawn | 0x468 | 0x498 | 0x28F9AB3 |
| **Chart da camada de ação (game/offsets.h, horse_diag)** | 0x110 | **0x118** | assinatura 0x9779CD |
| **Proteção remota: camada principal vazia** | só agia com chart | age se a camada 0 não tem ação | crashes 22:22 e 22:34 |

O item do chart é o mais importante: a proteção que segura o crash em 0x9A6391 lia o offset antigo e nunca agia.

## Ainda não verificado
- ~540 constantes sem instrução citada que contenha o valor (citam funções ou comentários). Ficam para verificação
  ao vivo quando o módulo correspondente der sinal no log.
- WorldAnchor +0x2E4/+0x2E8: o alinhamento sugere +0x198/+0x19C, mas a função mudou muito (r 0.44). O módulo está
  desligado; não mexi.
- P = [[WorldSystem]+0x60]: no 3663 o chamador pega P por outro caminho. Conferir ao vivo lendo a memória do convidado.

## Compilação
- Antes: o zip inteiro era descompactado a cada correção, todos os arquivos ganhavam data nova e tudo recompilava (~5 min).
- Agora: `push_delta.sh` (servidor) gera um zip só com os arquivos alterados e `apply-delta.ps1` (aqui) aplica.
