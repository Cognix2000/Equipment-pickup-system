#!/usr/bin/env python3
"""
Gerador de dados fictícios — Sistema de Retirada de Equipamentos (Netwise)

Gera, sempre com o mesmo resultado (semente fixa):
  planilha_retiradas_ficticia.xlsx  1ª aba = planilha do financeiro; demais abas = dados de apoio
  cidade_simulada.geojson           simula a API do mapa: bairros, ruas, POP, CTOs e clientes
  mock_hubsoft_api.json             respostas no formato da API do HubSoft (adaptador futuro)

Tudo é fictício: cidade, nomes, endereços, telefones (DDD 00, que não existe),
e-mails (domínio .invalid), números de série (prefixo SIMU) e MACs (faixa local 02:xx).
CPF/CNPJ têm dígito verificador ERRADO de propósito: não são documentos de ninguém
e só existem para testar o descarte na importação.
O centro da cidade fica em mar aberto de propósito, para nenhum pino cair sobre
um imóvel real. Para ver sobre um mapa de verdade, mude CENTRO_LAT/CENTRO_LNG.

Versão 2 (07/10/2026): colunas novas no fim da planilha do financeiro (CPF/CNPJ,
e-mail, contrato, ativação, rescisão, valor em aberto, login PPPoE), status pela
regra da rescisão e abas Devolucoes_na_Loja e Respostas_Simuladas. Os campos novos
usam um segundo gerador (rnd2): o mapa, as ordens e os equipamentos da versão 1
continuam idênticos.

Uso:        python3 gerar_dados_ficticios.py
Requisito:  openpyxl  (pip install openpyxl)
"""

import datetime as dt
import json
import math
import random
import unicodedata
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.utils import get_column_letter

# ------------------------------------------------------------------ parâmetros
SEMENTE = 20261006
CENTRO_LAT, CENTRO_LNG = -21.0, -39.0        # mar aberto, ~190 km da costa do ES
CIDADE, UF = "CIDADE SIMULADA", "MG"
N_CLIENTES_URBANOS = 160
N_ORDENS_URBANAS = 58                        # + 1 rural + 1 segundo serviço = 60 ordens
PORTAS_POR_CTO = 16
INICIO_ORDENS = dt.date(2026, 9, 14)
FIM_ORDENS = dt.date(2026, 10, 5)
TIPO_OS = "REMOÇÃO FTTH"
SAIDA = Path(__file__).resolve().parent

rnd = random.Random(SEMENTE)
rnd2 = random.Random(SEMENTE + 1)   # só para os campos da versão 2: não muda a sequência de rnd
M_LAT = 111_320.0
M_LNG = 111_320.0 * math.cos(math.radians(CENTRO_LAT))


def latlng(x, y):
    """Metros a leste (x) e ao norte (y) do centro -> (latitude, longitude)."""
    return round(CENTRO_LAT + y / M_LAT, 6), round(CENTRO_LNG + x / M_LNG, 6)


def sem_acento(texto):
    return "".join(c for c in unicodedata.normalize("NFD", texto) if unicodedata.category(c) != "Mn")


def escolhe(pares):
    return rnd.choices([p[0] for p in pares], weights=[p[1] for p in pares])[0]


# ------------------------------------------------------------------ bairros (grade 3 x 3)
LINHAS = [(400, 1100), (-400, 400), (-1100, -400)]          # norte, meio, sul
COLUNAS = [(-1500, -500), (-500, 500), (500, 1500)]         # oeste, meio, leste
NOMES_BAIRROS = [["ALTO DA SERRA", "BELA VISTA", "JARDIM PRIMAVERA"],
                 ["VILA OPERÁRIA", "CENTRO", "PARQUE DAS ÁGUAS"],
                 ["DISTRITO INDUSTRIAL", "RECANTO DO SOL", "SÃO JOSÉ"]]
SIGLAS = {"ALTO DA SERRA": "ALS", "BELA VISTA": "BVI", "JARDIM PRIMAVERA": "JPR",
          "VILA OPERÁRIA": "VOP", "CENTRO": "CEN", "PARQUE DAS ÁGUAS": "PAG",
          "DISTRITO INDUSTRIAL": "DIN", "RECANTO DO SOL": "RSO", "SÃO JOSÉ": "SJO"}
DENSIDADE = {"CENTRO": 1.0, "DISTRITO INDUSTRIAL": 0.25}      # demais bairros: 0.7

bairros = []
for i, (y0, y1) in enumerate(LINHAS):
    for j, (x0, x1) in enumerate(COLUNAS):
        nome = NOMES_BAIRROS[i][j]
        bairros.append({"nome": nome, "sigla": SIGLAS[nome], "x0": x0, "x1": x1, "y0": y0, "y1": y1,
                        "cep": f"00000-{(len(bairros) + 1) * 10:03d}"})
ZONA_RURAL = {"nome": "ZONA RURAL", "sigla": "RUR", "cep": "00000-100"}


def bairro_de(x, y):
    for b in bairros:
        if b["x0"] <= x < b["x1"] and b["y0"] <= y < b["y1"]:
            return b
    return None


# ------------------------------------------------------------------ ruas (quadras de 200 m)
ARVORES = ["RUA DAS ACÁCIAS", "RUA DOS IPÊS", "RUA DOS JACARANDÁS", "RUA DAS PALMEIRAS",
           "RUA DAS QUARESMEIRAS", "RUA DAS MANGUEIRAS", "RUA DAS JABUTICABEIRAS", "RUA DOS CEDROS",
           "RUA DAS AROEIRAS", "RUA DAS PAINEIRAS", "RUA DAS SIBIPIRUNAS", "RUA DOS FLAMBOYANTS",
           "RUA DOS OITIS", "RUA DAS PITANGUEIRAS"]
AVES = ["RUA SABIÁ", "RUA BEM-TE-VI", "RUA CANÁRIO", "RUA BEIJA-FLOR", "RUA JOÃO-DE-BARRO",
        "RUA TICO-TICO", "RUA SANHAÇO", "RUA JURITI", "RUA SERIEMA", "RUA TUCANO"]

ruas = []
_nomes = iter(ARVORES)
for _x in range(-1400, 1401, 200):                        # norte-sul
    ruas.append({"nome": "AVENIDA CENTRAL" if _x == 0 else next(_nomes),
                 "eixo": "NS", "pos": _x, "ini": -1100, "fim": 1100})
_nomes = iter(AVES)
for _y in range(-1000, 1001, 200):                        # leste-oeste
    ruas.append({"nome": "AVENIDA DA LAGOA" if _y == 0 else next(_nomes),
                 "eixo": "LO", "pos": _y, "ini": -1500, "fim": 1500})
ESTRADA = {"nome": "ESTRADA MUNICIPAL DO RETIRO", "eixo": "LO", "pos": -200, "ini": 1500, "fim": 3600}
POP = {"nome": "POP CENTRO — SEDE E ESTOQUE", "x": 100, "y": -60}


def ponto_na_rua(rua, t, lado):
    """t = posição ao longo da rua; lado = +1/-1 (casa a 9 m do eixo)."""
    if rua["eixo"] == "NS":
        return rua["pos"] + 9 * lado, t
    return t, rua["pos"] + 9 * lado


def numero_casa(rua, t, lado):
    """Numeração métrica a partir do início da rua: ímpar de um lado, par do outro."""
    n = max(2, int(round(t - rua["ini"])))
    if (lado > 0) != (n % 2 == 1):
        n += 1
    return str(n)


def rua_cruzada(rua, t):
    alvo = round(t / 200) * 200
    eixo = "LO" if rua["eixo"] == "NS" else "NS"
    for r in ruas:
        if r["eixo"] == eixo and r["pos"] == alvo:
            return r["nome"], abs(t - alvo)
    return None, None


# ------------------------------------------------------------------ pessoas e cadastros
NOMES_F = ["MARIA", "ANA", "JULIANA", "PATRÍCIA", "FERNANDA", "ALINE", "CAMILA", "BRUNA", "LETÍCIA",
           "SANDRA", "LUCIANA", "VANESSA", "RENATA", "TATIANE", "CLÁUDIA", "DÉBORA", "ELAINE", "GABRIELA",
           "HELENA", "ISABELA", "JÉSSICA", "KÁTIA", "LARISSA", "MÔNICA", "NATÁLIA", "PRISCILA",
           "ROSÂNGELA", "SIMONE", "TERESA", "VALÉRIA"]
NOMES_M = ["JOSÉ", "JOÃO", "ANTÔNIO", "CARLOS", "PAULO", "LUCAS", "MARCOS", "LUIZ", "GABRIEL",
           "RAFAEL", "DANIEL", "MARCELO", "BRUNO", "EDUARDO", "FELIPE", "RODRIGO", "GUSTAVO", "LEANDRO",
           "FÁBIO", "ANDRÉ", "DIEGO", "RICARDO", "SÉRGIO", "THIAGO", "VINÍCIUS", "WAGNER", "ALEXANDRE",
           "CLEBER", "EDSON", "HUGO"]
SOBRENOMES = ["SILVA", "SANTOS", "OLIVEIRA", "SOUZA", "RODRIGUES", "FERREIRA", "ALVES", "PEREIRA",
              "LIMA", "GOMES", "COSTA", "RIBEIRO", "MARTINS", "CARVALHO", "ALMEIDA", "LOPES", "SOARES",
              "FERNANDES", "VIEIRA", "BARBOSA", "ROCHA", "DIAS", "NASCIMENTO", "ANDRADE", "MOREIRA",
              "NUNES", "MARQUES", "MACHADO", "FREITAS", "CARDOSO", "RAMOS", "GONÇALVES", "SANTANA",
              "TEIXEIRA", "MOURA", "CAMPOS", "PINTO", "CUNHA", "REZENDE", "AMARAL"]
EMPRESAS = ["OFICINA EXEMPLO LTDA", "MERCEARIA EXEMPLO ME", "CLÍNICA EXEMPLO LTDA", "ESCRITÓRIO EXEMPLO LTDA"]
PLANOS = [("FIBRA 200 MEGA", 79.90, 25), ("FIBRA 400 MEGA", 99.90, 40),
          ("FIBRA 600 MEGA", 119.90, 25), ("FIBRA 1 GIGA", 149.90, 10)]
COMPLEMENTOS = [("", 70), ("CASA 2", 8), ("FUNDOS", 7), ("APTO 101", 5), ("APTO 202", 4),
                ("BLOCO B APTO 12", 3), ("SOBRADO", 3)]
REFERENCIAS = [("", 60), ("PORTÃO AZUL", 6), ("CASA DE MURO AMARELO", 6), ("EM FRENTE À PRAÇA", 5),
               ("AO LADO DA ESCOLA", 5), ("PERTO DO POSTO DE SAÚDE", 5), ("ESQUINA", 13)]
OBSERVACOES = [("", 50), ("LIGAR ANTES DE IR", 10), ("CLIENTE SÓ FICA EM CASA À TARDE", 7),
               ("CACHORRO NO QUINTAL", 7), ("PEDIR PARA CHAMAR O TITULAR", 6),
               ("EQUIPAMENTO FICA NA SALA", 6), ("PRÉDIO COM PORTEIRO", 4)]
STATUS_TEXTO = {"suspenso_debito": "Suspenso por débito", "cancelado": "Cancelado",
                "servico_habilitado": "Serviço habilitado"}

_contador = {"codigo": 1000, "id_cliente": 50000, "id_servico": 70000}
_nomes_usados = set()
_enderecos_usados = set()


def proximo(chave):
    _contador[chave] += 1
    return _contador[chave]


def novo_nome():
    while True:
        partes = [rnd.choice(NOMES_F if rnd.random() < 0.5 else NOMES_M)]
        if rnd.random() < 0.35:
            partes.append(rnd.choice(SOBRENOMES))
        partes.append(rnd.choice(SOBRENOMES))
        nome = " ".join(partes)
        if nome not in _nomes_usados and len(set(partes)) == len(partes):
            _nomes_usados.add(nome)
            return nome


def telefone(bloco):
    """11 dígitos no formato do HubSoft (DDNNNNNNNNN), sempre com DDD 00."""
    return f"009{bloco:04d}{rnd.randint(0, 9999):04d}"


def fmt_tel(d, estilo=0):
    if not d:
        return ""
    if estilo == 1:
        return f"{d[:2]} {d[2:7]}-{d[7:]}"
    if estilo == 2:
        return d
    if estilo == 3:
        return f"({d[:2]}){d[2:7]} {d[7:]}"
    return f"({d[:2]}) {d[2:7]}-{d[7:]}"


def novo_cliente(rua, t, lado, bairro, numero, empresa=None):
    x, y = ponto_na_rua(rua, t, lado)
    lat, lng = latlng(x, y)
    codigo = proximo("codigo")
    nome = empresa or novo_nome()
    referencia = escolhe(REFERENCIAS)
    if referencia == "ESQUINA":
        cruzada, distancia = rua_cruzada(rua, t)
        referencia = f"PRÓXIMO À ESQUINA COM {cruzada}" if cruzada and distancia < 60 else ""
    plano, valor, _ = rnd.choices(PLANOS, weights=[p[2] for p in PLANOS])[0]
    primeiro, ultimo = (sem_acento(p).lower() for p in (nome.split()[0], nome.split()[-1]))
    return {
        "id_cliente": proximo("id_cliente"), "codigo_cliente": codigo, "id_cliente_servico": proximo("id_servico"),
        "nome": nome, "tipo_pessoa": "pj" if empresa else "pf",
        "tel1": telefone(codigo), "tel2": telefone(rnd.randint(5000, 9999)) if rnd.random() < 0.3 else "",
        "email": f"{primeiro}.{ultimo}.{codigo}@exemplo.invalid" if rnd.random() < 0.5 else "",
        "rua": rua["nome"], "numero": numero,
        "complemento": rnd.choice(["LOJA 1", "SALA 3"]) if empresa else escolhe(COMPLEMENTOS),
        "bairro": bairro["nome"], "cep": bairro["cep"], "referencia": referencia,
        "x": x, "y": y, "lat": lat, "lng": lng,
        "plano": plano, "valor_plano": valor, "numero_plano": 1,
        "status_servico": "servico_habilitado", "no_mapa": True,
    }


def sorteia_endereco(excluir_bairro=None):
    while True:
        rua = rnd.choice(ruas)
        t = rnd.uniform(rua["ini"] + 20, rua["fim"] - 20)
        if not 18 < t % 200 < 182:                 # longe das esquinas
            continue
        lado = rnd.choice((-1, 1))
        b = bairro_de(*ponto_na_rua(rua, t, lado))
        if b is None or b["nome"] == excluir_bairro or rnd.random() > DENSIDADE.get(b["nome"], 0.7):
            continue
        numero = numero_casa(rua, t, lado)
        if (rua["nome"], numero) in _enderecos_usados:
            continue
        _enderecos_usados.add((rua["nome"], numero))
        return rua, t, lado, b, numero


# ------------------------------------------------------------------ CTOs
def nova_cto(ident, nome, bairro, x, y):
    lat, lng = latlng(x, y)
    return {"id": ident, "nome": nome, "bairro": bairro, "x": x, "y": y, "lat": lat, "lng": lng, "ocupadas": 0}


def gera_ctos():
    ctos, ident = [], 100
    for b in bairros:
        xs = [r["pos"] for r in ruas if r["eixo"] == "NS" and b["x0"] + 40 < r["pos"] < b["x1"] - 40]
        ys = [r["pos"] for r in ruas if r["eixo"] == "LO" and b["y0"] + 40 < r["pos"] < b["y1"] - 40]
        meio_y = (b["y0"] + b["y1"]) / 2
        escolhidos = []
        for fracao in (1 / 3, 2 / 3):
            alvo = (b["x0"] + (b["x1"] - b["x0"]) * fracao, meio_y)
            escolhidos.append(min(((x, y) for x in xs for y in ys if (x, y) not in escolhidos),
                                  key=lambda p: math.dist(p, alvo)))
        for n, (x, y) in enumerate(escolhidos, 1):
            ident += 1
            ctos.append(nova_cto(ident, f"CTO-{b['sigla']}-{n:02d}", b["nome"], x + 4, y + 4))
    ctos.append(nova_cto(ident + 1, "CTO-RUR-01", ZONA_RURAL["nome"], 3100, -196))
    return ctos


def liga_ctos(clientes_mapa, ctos):
    for c in sorted(clientes_mapa, key=lambda c: c["id_cliente_servico"]):
        for cto in sorted(ctos, key=lambda k: math.dist((k["x"], k["y"]), (c["x"], c["y"]))):
            if cto["ocupadas"] < PORTAS_POR_CTO:
                cto["ocupadas"] += 1
                c["cto"], c["porta"] = cto, cto["ocupadas"]
                c["dist_cto"] = round(math.dist((cto["x"], cto["y"]), (c["x"], c["y"])))
                break


# ------------------------------------------------------------------ equipamentos em comodato
PRODUTOS = {  # id_produto: (nome, categoria, patrimonial, valor de referência fictício em R$)
    201: ("ONU GPON 1 PORTA", "ONU", True, 120.00),
    202: ("ONT GPON WI-FI AC", "ONT", True, 280.00),
    203: ("ONT GPON WI-FI 6", "ONT", True, 420.00),
    204: ("ROTEADOR WI-FI AC1200", "ROTEADOR", True, 180.00),
    205: ("REPETIDOR WI-FI MESH", "REPETIDOR", True, 220.00),
    206: ("FONTE 12V 1A", "ACESSÓRIO", False, 25.00),
}
KITS = [((202,), 1, 45), ((203,), 1, 20), ((201, 204), 2, 25), ((202, 205), 2, 10)]  # itens, fontes, peso


def gera_equipamentos(ordens):
    itens, id_item, codigo_item, series = [], 300000, 80000, set()
    for o in ordens:
        patrimoniais, fontes, _ = rnd.choices(KITS, weights=[k[2] for k in KITS])[0]
        for id_produto in patrimoniais:
            id_item += 1
            codigo_item += 1
            while True:
                serie = "SIMU" + "".join(rnd.choice("0123456789ABCDEF") for _ in range(8))
                if serie not in series:
                    series.add(serie)
                    break
            mac = "02:" + ":".join(f"{rnd.randint(0, 255):02X}" for _ in range(5))
            itens.append({"ordem": o, "id_produto": id_produto, "id_produto_item": id_item,
                          "codigo_item": codigo_item, "numero_serie": serie, "mac": mac, "quantidade": 1})
        itens.append({"ordem": o, "id_produto": 206, "id_produto_item": None, "codigo_item": None,
                      "numero_serie": "", "mac": "", "quantidade": fontes})
    return itens


# ------------------------------------------------------------------ ordens de retirada
def dia_util():
    while True:
        dia = INICIO_ORDENS + dt.timedelta(days=rnd.randint(0, (FIM_ORDENS - INICIO_ORDENS).days))
        if dia.weekday() < 5:
            return dia


def gera_ordens(clientes_ordem):
    ordens, protocolos = [], set()
    for c in clientes_ordem:
        aberto = dt.datetime.combine(dia_util(), dt.time(rnd.randint(8, 17), rnd.randint(0, 59), rnd.randint(0, 59)))
        while True:
            protocolo = f"{aberto:%Y%m%d%H%M%S}{rnd.randint(0, 999999):06d}"
            if protocolo not in protocolos:
                protocolos.add(protocolo)
                break
        atraso = rnd.randint(35, 180)
        c["status_servico"] = "cancelado" if atraso >= 90 else "suspenso_debito"
        ordens.append({"protocolo": protocolo, "aberto": aberto, "cliente": c, "atraso": atraso,
                       "obs": escolhe(OBSERVACOES), "tel1": fmt_tel(c["tel1"]), "tel2": fmt_tel(c["tel2"])})
    ordens.sort(key=lambda o: o["aberto"])
    for n, o in enumerate(ordens, 1):
        o["numero_os"], o["id_ordem_servico"], o["id_atendimento"] = 5100 + n, 9100 + n, 12100 + n
    return ordens


# ------------------------------------------------------------------ montagem dos dados
def montar():
    urbanos = []
    empresas = list(EMPRESAS)
    while len(urbanos) < N_CLIENTES_URBANOS:
        rua, t, lado, b, numero = sorteia_endereco()
        empresa = empresas.pop() if empresas and len(urbanos) % 40 == 7 else None
        urbanos.append(novo_cliente(rua, t, lado, b, numero, empresa))

    # cliente da zona rural, fora do perímetro urbano
    rural = novo_cliente(ESTRADA, 3150, 1, ZONA_RURAL, "S/N")
    rural.update(complemento="SÍTIO BOA ESPERANÇA", referencia="KM 2, PORTEIRA VERDE")

    pf = [c for c in urbanos if c["tipo_pessoa"] == "pf"]
    pj = [c for c in urbanos if c["tipo_pessoa"] == "pj"]
    escolhidos = rnd.sample(pf, N_ORDENS_URBANAS - 1) + pj[:1]

    # dois clientes com ordem que ainda não existem no mapa (cenário "sem localização")
    sem_mapa = escolhidos[3:5]
    for c in sem_mapa:
        c["no_mapa"] = False
    sem_mapa[1]["numero"] = "S/N"

    # um cliente com um segundo serviço em outro endereço (duas ordens, mesmo código)
    base = escolhidos[10]
    rua, t, lado, b, numero = sorteia_endereco(excluir_bairro=base["bairro"])
    segundo = novo_cliente(rua, t, lado, b, numero)
    _nomes_usados.discard(segundo["nome"])
    _contador["codigo"] -= 1
    _contador["id_cliente"] -= 1
    segundo.update({k: base[k] for k in ("id_cliente", "codigo_cliente", "nome", "tipo_pessoa", "tel1", "tel2", "email")})
    segundo["numero_plano"] = 2

    clientes = urbanos + [rural, segundo]
    ctos = gera_ctos()
    liga_ctos([c for c in clientes if c["no_mapa"]], ctos)

    ordens = gera_ordens(escolhidos + [rural, segundo])
    equipamentos = gera_equipamentos(ordens)

    # bagunça realista na planilha do financeiro (testa a normalização do importador)
    comuns = [o for o in ordens if o["cliente"] not in sem_mapa + [rural, segundo, base] and o["cliente"]["tipo_pessoa"] == "pf"]
    for o, estilo in zip(rnd.sample(comuns, 6), (1, 1, 2, 2, 3, 3)):
        o["tel1"] = fmt_tel(o["cliente"]["tel1"], estilo)
    restantes = [o for o in comuns if o["tel1"] == fmt_tel(o["cliente"]["tel1"])]
    sem_tel1 = rnd.sample(restantes, 2)
    for o in sem_tel1:
        o["tel1"], o["tel2"] = "", fmt_tel(o["cliente"]["tel2"] or telefone(rnd.randint(5000, 9999)))
    restantes = [o for o in restantes if o not in sem_tel1]
    tel_invalido = rnd.choice(restantes)
    tel_invalido["tel1"] = "(00) 9123"
    restantes = [o for o in restantes if o is not tel_invalido]
    formula = rnd.choice(restantes)
    formula["obs"] = "=1+2 (teste: deve ficar como texto)"
    restantes = [o for o in restantes if o is not formula]
    duplicada = rnd.choice(restantes)
    restantes = [o for o in restantes if o is not duplicada]

    pagamentos = []
    momentos = [
        ("antes do aceite", "O financeiro registra o pagamento e a ordem sai da lista de disponíveis (automático só com o HubSoft integrado)."),
        ("depois do aceite, antes de sair", "Ordem cancelada; quem aceitou é avisado na hora."),
        ("com o técnico em rota", "A revalidação online bloqueia 'Iniciar retirada' e mostra 'cliente pagou'."),
        ("depois da retirada concluída", "Ordem continua concluída; alerta ao financeiro para tratar a reinstalação fora do sistema."),
    ]
    for n, (o, (momento, esperado)) in enumerate(zip(rnd.sample(restantes, 4), momentos), 1):
        pago = dt.datetime(2026, 10, 7, 9, 0) + dt.timedelta(hours=2 * n, minutes=rnd.randint(0, 50))
        pagamentos.append({"evento": f"P{n}", "ordem": o, "pago": pago, "momento": momento, "esperado": esperado})

    especiais = {"sem_mapa": sem_mapa, "rural": rural, "segundo": segundo, "base": base,
                 "sem_tel1": sem_tel1, "tel_invalido": tel_invalido, "formula": formula,
                 "duplicada": duplicada}
    return clientes, ctos, ordens, equipamentos, pagamentos, especiais


# ------------------------------------------------------------------ versão 2: campos novos (só rnd2)
LOJA = "LOJA CENTRO (SIMULADA)"


def _dv(digitos, pesos):
    resto = sum(int(d) * p for d, p in zip(digitos, pesos)) % 11
    return 0 if resto < 2 else 11 - resto


def cpf_invalido():
    """CPF com o 1º dígito verificador ERRADO de propósito: nunca é o documento de alguém."""
    nove = "".join(str(rnd2.randint(0, 9)) for _ in range(9))
    d1 = _dv(nove, range(10, 1, -1))
    d2 = _dv(nove + str(d1), range(11, 1, -1))
    d = f"{nove}{(d1 + 1) % 10}{d2}"
    return f"{d[:3]}.{d[3:6]}.{d[6:9]}-{d[9:]}"


def cnpj_invalido():
    """CNPJ com o 1º dígito verificador ERRADO de propósito."""
    doze = "".join(str(rnd2.randint(0, 9)) for _ in range(8)) + "0001"
    pesos = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]
    d1 = _dv(doze, pesos)
    d2 = _dv(doze + str(d1), [6] + pesos)
    d = f"{doze}{(d1 + 1) % 10}{d2}"
    return f"{d[:2]}.{d[2:5]}.{d[5:8]}/{d[8:12]}-{d[12:]}"


def documento_valido(doc):
    """Confere os dois dígitos verificadores (usado só na verificação dos dados gerados)."""
    d = "".join(ch for ch in doc if ch.isdigit())
    if len(d) == 11:
        return _dv(d[:9], range(10, 1, -1)) == int(d[9]) and _dv(d[:10], range(11, 1, -1)) == int(d[10])
    pesos = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]
    return _dv(d[:12], pesos) == int(d[12]) and _dv(d[:13], [6] + pesos) == int(d[13])


def enriquecer(ordens, equipamentos, pagamentos, especiais):
    """Regra da rescisão, colunas novas e as abas de devolução e de contato. Usa só rnd2."""
    e = especiais
    fixas = [e["tel_invalido"], e["formula"], e["duplicada"], *e["sem_tel1"], *(p["ordem"] for p in pagamentos)]
    fixas += [o for o in ordens if o["cliente"] in e["sem_mapa"] + [e["rural"], e["segundo"], e["base"]]]
    livres = [o for o in ordens if all(o is not f for f in fixas)]

    # Quase todas as ordens nascem na rescisão (cl. 9.5). Cinco ainda estão só suspensas:
    # testam o alerta do importador e a exceção do gestor.
    excecoes = rnd2.sample(livres, 5)
    documentos = {}
    for o in ordens:
        c = o["cliente"]
        if c["codigo_cliente"] not in documentos:
            documentos[c["codigo_cliente"]] = cnpj_invalido() if c["tipo_pessoa"] == "pj" else cpf_invalido()
        c["cpf_cnpj"] = documentos[c["codigo_cliente"]]
        c["contrato"] = f"CT{c['codigo_cliente']}-{c['numero_plano']}"
        c["ativacao"] = dt.date(2019, 1, 1) + dt.timedelta(days=rnd2.randint(0, 2555))
        primeiro = sem_acento(c["nome"].split()[0]).lower()
        c["login_pppoe"] = f"{primeiro}.{c['codigo_cliente']}" + (f".{c['numero_plano']}" if c["numero_plano"] > 1 else "")
        if any(o is x for x in excecoes):
            c["status_servico"] = "suspenso_debito"
            o["atraso"] = rnd2.randint(40, 75)
            o["rescisao"] = None
        else:
            c["status_servico"] = "cancelado"
            o["atraso"] = rnd2.randint(80, 180)
            o["rescisao"] = o["aberto"].date()          # a OS de retirada abre no dia da rescisão
        o["valor_aberto"] = round(c["valor_plano"] * max(1, round(o["atraso"] / 30)), 2)

    def series(o):
        return [x["numero_serie"] for x in equipamentos if x["ordem"] is o and x["id_produto_item"]]

    candidatas = [o for o in livres if all(o is not x for x in excecoes)]
    com_dois = [o for o in candidatas if len(series(o)) == 2]
    d1, d2 = rnd2.sample([o for o in candidatas if o not in com_dois], 2)
    d3 = rnd2.choice(com_dois)
    devolucoes = [
        {"evento": "D1", "ordem": d1, "quando": d1["aberto"] + dt.timedelta(hours=26, minutes=rnd2.randint(0, 50)),
         "devolvidos": series(d1), "faltam": [], "momento": "Dentro da carência de 48 h",
         "esperado": "A ordem vira Devolvida na loja sem nunca ter sido publicada; nenhum técnico ou parceiro a vê."},
        {"evento": "D2", "ordem": d2, "quando": d2["aberto"] + dt.timedelta(days=3, hours=2, minutes=rnd2.randint(0, 50)),
         "devolvidos": series(d2), "faltam": [], "momento": "Depois da publicação",
         "esperado": "A ordem vira Devolvida na loja e quem estava com ela é avisado na hora; a visita sai da agenda."},
        {"evento": "D3", "ordem": d3, "quando": d3["aberto"] + dt.timedelta(days=2, hours=5, minutes=rnd2.randint(0, 50)),
         "devolvidos": series(d3)[:1], "faltam": series(d3)[1:], "momento": "Devolução parcial",
         "esperado": "A ordem continua aberta só para o item que falta; o item devolvido sai da lista de retirada."},
    ]

    r = rnd2.sample([o for o in candidatas if all(o is not d for d in (d1, d2, d3))], 8)
    respostas = [
        ("R01", e["tel_invalido"], e["tel_invalido"]["tel1"], "Importação", "Formato inválido (número incompleto)",
         "Marca o número como inválido; a cadência pula mensagens e ligações e segue por visita"),
        ("R02", e["sem_tel1"][0], e["sem_tel1"][0]["tel2"], "Mensagem 1 (aviso)", "Telefone 1 vazio; Telefone 2 válido",
         "Usa o Telefone 2 em toda a cadência"),
        ("R03", r[0], r[0]["tel1"], "1ª ligação", "Operadora: número inexistente",
         "Marca a linha como inexistente; tenta o Telefone 2 ou segue por visita"),
        ("R04", r[1], r[1]["tel1"], "Mensagem 1 (aviso)", "O wa.me avisa que o número não está no WhatsApp",
         "O operador marca 'sem WhatsApp'; a cadência segue só por ligação"),
        ("R05", r[2], r[2]["tel1"], "Mensagem 1 pela API oficial (depois da V1)", "Erro 131026: mensagem não entregue",
         "Marca 'WhatsApp não entregou' e segue por ligação; não conclui que o número não existe"),
        ("R06", r[3], r[3]["tel1"], "Mensagem 1 (aviso)", "Respondeu: 'pode vir quinta à tarde'",
         "Agenda quinta à tarde; a ordem sai da cadência e a mensagem 3 (confirmação) sai na véspera"),
        ("R07", r[4], r[4]["tel1"], "Mensagem 1 (aviso)", "Respondeu: 'vou levar na loja amanhã'",
         "Aguarda a carência; sem devolução, a cadência continua no dia 2"),
        ("R08", r[5], r[5]["tel1"], "Cadência completa", "Nenhuma resposta e 3 visitas sem ninguém em casa",
         "No limite, vira Encerrada sem retirada e entra na lista de cobrança"),
        ("R09", r[6], r[6]["tel1"], "2ª ligação", "Atendeu e quer negociar a dívida",
         "Encaminha à central como reversão; quem negocia é o financeiro; bônus só depois da 1ª parcela paga"),
        ("R10", r[7], r[7]["tel1"], "2ª ligação", "Operador tenta ligar num domingo às 21h",
         "O app não oferece ligar nem enviar fora da janela (segunda a sábado, 8h às 18h); registro manual fica marcado para o gestor"),
    ]
    return {"excecoes": excecoes, "devolucoes": devolucoes, "respostas": respostas}


# ------------------------------------------------------------------ planilha (xlsx)
FONTE = "Arial"
CAB_FILL = PatternFill("solid", fgColor="1F3A5F")
CAB_FONT = Font(name=FONTE, bold=True, color="FFFFFF", size=10)
CEL_FONT = Font(name=FONTE, size=10)


class Formula(str):
    """Marca um texto que deve virar fórmula (o resto que começa com '=' fica como texto)."""


def escrever_aba(ws, cabecalho, linhas, texto=(), datas=(), moeda=(), larguras=None, dias=()):
    """texto/datas/moeda/dias: letras de coluna ("AGH" ou ("A", "AA")); dias = só a data, sem hora."""
    texto, datas, moeda, dias = (set(x) for x in (texto, datas, moeda, dias))
    ws.append(cabecalho)
    for linha in linhas:
        ws.append([str(v) if isinstance(v, Formula) else v for v in linha])
        for c, v in zip(ws[ws.max_row], linha):
            if isinstance(v, str) and not isinstance(v, Formula) and v.startswith("="):
                c.data_type = "s"                       # texto que parece fórmula continua texto
    for c in ws[1]:
        c.font, c.fill = CAB_FONT, CAB_FILL
        c.alignment = Alignment(vertical="center", wrap_text=True)
    ws.row_dimensions[1].height = 30
    for row in ws.iter_rows(min_row=2):
        for c in row:
            c.font = CEL_FONT
            col = c.column_letter
            if col in texto:
                c.number_format = "@"
            elif col in datas:
                c.number_format = "DD/MM/YYYY HH:MM"
            elif col in dias:
                c.number_format = "DD/MM/YYYY"
            elif col in moeda:
                c.number_format = "#,##0.00"
    for i, titulo in enumerate(cabecalho, 1):
        letra = get_column_letter(i)
        if larguras and letra in larguras:
            largura = larguras[letra]
        else:
            maior = max([len(str(titulo))] + [len(str(r[i - 1])) for r in linhas if r[i - 1] is not None])
            largura = min(max(10, maior + 2), 48)
        ws.column_dimensions[letra].width = largura
    ws.freeze_panes = "A2"
    ws.auto_filter.ref = ws.dimensions


def gerar_planilha(clientes, ctos, ordens, equipamentos, pagamentos, especiais, extras, caminho):
    wb = Workbook()

    # 1) planilha do financeiro — primeira aba, como o arquivo real.
    #    As colunas U a AA (versão 2) imitam o que a planilha real traz a mais sobre o cliente;
    #    o importador descarta as que a retirada não usa.
    ws = wb.active
    ws.title = "Ordens_Remocao"
    cab = ["Protocolo", "Nº OS", "Data de abertura", "Tipo de OS", "Código do cliente", "Cliente",
           "Telefone 1", "Telefone 2", "Endereço", "Número", "Complemento", "Bairro", "Cidade", "UF",
           "CEP", "Referência", "Plano", "Status do serviço", "Dias em atraso", "Observação",
           "CPF/CNPJ", "E-mail", "Nº do contrato", "Data de ativação", "Data da rescisão",
           "Valor em aberto (R$)", "Login PPPoE"]

    def linha_ordem(o):
        c = o["cliente"]
        return [o["protocolo"], o["numero_os"], o["aberto"], TIPO_OS, c["codigo_cliente"], c["nome"],
                o["tel1"], o["tel2"], c["rua"], c["numero"], c["complemento"], c["bairro"], CIDADE, UF,
                c["cep"], c["referencia"], c["plano"], STATUS_TEXTO[c["status_servico"]], o["atraso"], o["obs"],
                c["cpf_cnpj"], c["email"], c["contrato"], c["ativacao"], o["rescisao"], o["valor_aberto"],
                c["login_pppoe"]]

    linhas = [linha_ordem(o) for o in ordens] + [linha_ordem(especiais["duplicada"])]
    escrever_aba(ws, cab, linhas, texto=("A", "G", "H", "J", "O", "U", "W", "AA"), datas="C",
                 dias=("X", "Y"), moeda=("Z",), larguras={"T": 40, "V": 34, "X": 14, "Y": 14, "Z": 14})

    # 2) leia-me com resumo calculado por fórmulas
    leia = wb.create_sheet("LEIA-ME")
    leia.column_dimensions["A"].width = 44
    leia.column_dimensions["B"].width = 112
    conteudo = [
        ("Dados fictícios — Sistema de Retirada de Equipamentos", None),
        ("Gerado por gerar_dados_ficticios.py (semente 20261006). Nada aqui corresponde a pessoas, "
         "endereços, telefones ou equipamentos reais.", None),
        (None, None),
        ("Aba", "O que simula"),
        ("Ordens_Remocao", "A planilha que o financeiro envia hoje (uma linha por ordem). É o arquivo de teste do importador."),
        ("Equipamentos_Comodato", "Equipamentos vinculados a cada serviço. No HubSoft: Estoque › Produtos vinculados › Serviço do cliente."),
        ("Localizacoes", "Onde fica cada cliente e em qual CTO e porta ele está (simula a API do mapa da terceirizada)."),
        ("CTOs", "Caixas de terminação óptica da cidade simulada, com portas ocupadas calculadas por fórmula."),
        ("Equipe", "Usuários fictícios para testar login, perfis e parceiros (ativo, pendente, revogado)."),
        ("Pagamentos_Simulados", "Clientes que pagam depois que a ordem já existe. Testa o cancelamento em cada etapa."),
        ("Devolucoes_na_Loja", "Clientes que levam o equipamento à loja: dentro da carência, depois da publicação e devolução parcial."),
        ("Respostas_Simuladas", "Resultados de contato para o modo simulação: número inválido, sem WhatsApp, resposta, silêncio, fora do horário."),
        ("Cenarios_de_Teste", "Casos de teste com os dados que exercitam cada um e o resultado esperado."),
        ("Dicionario_de_Dados", "Cada coluna, o campo equivalente na API do HubSoft e quem pode ver no sistema."),
        (None, None),
        ("Resumo (fórmulas)", None),
        ("Linhas na planilha do financeiro", Formula("=COUNTA(Ordens_Remocao!A2:A1000)")),
        ("Protocolos únicos", Formula('=SUMPRODUCT((Ordens_Remocao!A2:A1000<>"")/COUNTIF(Ordens_Remocao!A2:A1000,Ordens_Remocao!A2:A1000&""))')),
        ("Linhas de clientes rescindidos (status Cancelado)", Formula('=COUNTIF(Ordens_Remocao!R2:R1000,"Cancelado")')),
        ("Linhas sem data de rescisão (alerta na importação)", Formula('=SUMPRODUCT((Ordens_Remocao!A2:A1000<>"")*(Ordens_Remocao!Y2:Y1000=""))')),
        ("Clientes no mapa", Formula("=COUNTA(Localizacoes!A2:A1000)")),
        ("CTOs", Formula("=COUNTA(CTOs!A2:A1000)")),
        ("Itens patrimoniais em comodato", Formula('=COUNTIF(Equipamentos_Comodato!G2:G1000,"sim")')),
        ("Valor de referência em comodato (R$, fictício)", Formula("=SUM(Equipamentos_Comodato!O2:O1000)")),
        ("Devoluções na loja simuladas", Formula("=COUNTA(Devolucoes_na_Loja!A2:A100)")),
        ("Casos de contato simulados", Formula("=COUNTA(Respostas_Simuladas!A2:A100)")),
        (None, None),
        ("Regras dos dados fictícios", None),
        ("Telefones", "DDD 00, que não existe: nenhum teste liga ou manda WhatsApp para alguém real."),
        ("Modo simulação", "Aceita DDD 00 como válido para testar a cadência de contato. Em produção, DDD 00 é inválido."),
        ("E-mails", "Domínio .invalid, reservado para testes: nenhuma mensagem é entregue."),
        ("Equipamentos", "Nº de série com prefixo SIMU e MAC na faixa local 02:xx (não coincide com fabricantes)."),
        ("Localização", f"Centro da cidade em {CENTRO_LAT:.4f}, {CENTRO_LNG:.4f} (mar aberto): nenhum pino cai sobre "
                        "imóvel real. Para mudar, edite CENTRO_LAT e CENTRO_LNG no script e rode de novo."),
        ("CPF/CNPJ", "Fictícios e com dígito verificador errado de propósito: não são documentos de ninguém. "
                     "Existem só para testar o descarte na importação (minimização, LGPD)."),
        ("Login PPPoE", "Fictício (nome.código). É credencial de acesso: o importador descarta e nunca grava."),
        ("Rescisão e carência", "A OS de retirada abre no dia da rescisão; a carência de 48 h (cl. 15.2) conta da abertura da OS."),
        ("Coordenadas", "O GeoJSON segue o padrão [longitude, latitude]; as abas trazem latitude e longitude separadas."),
        ("Valores em R$", "Valores de equipamentos e dívidas são fictícios, só para testar priorização, relatórios e descarte."),
        ("Portas por CTO", f"{PORTAS_POR_CTO} portas em todas as CTOs (premissa da simulação)."),
    ]
    for a, b in conteudo:
        leia.append([a, str(b) if isinstance(b, Formula) else b])
    for row in leia.iter_rows():
        for c in row:
            c.font = CEL_FONT
            c.alignment = Alignment(wrap_text=True, vertical="top")
    leia["A1"].font = Font(name=FONTE, bold=True, size=14)
    for r, (a, b) in enumerate(conteudo, 1):
        if a in ("Aba", "Resumo (fórmulas)", "Regras dos dados fictícios"):
            for c in leia[r]:
                c.font = Font(name=FONTE, bold=True, size=10)
        if isinstance(b, Formula):
            leia.cell(row=r, column=2).alignment = Alignment(horizontal="left")
            if "R$" in a:
                leia.cell(row=r, column=2).number_format = "#,##0.00"

    # 3) equipamentos em comodato (simula estoque › produtos vinculados ao serviço)
    ws = wb.create_sheet("Equipamentos_Comodato")
    cab = ["Protocolo", "Código do cliente", "ID serviço do cliente", "ID produto", "Produto", "Categoria",
           "Patrimonial", "ID item (patrimônio)", "Código do item", "Nº de série", "MAC", "Quantidade",
           "Status do item", "Valor unitário de referência (R$)", "Valor total de referência (R$)"]
    linhas = []
    for n, e in enumerate(equipamentos, 2):
        nome, categoria, patrimonial, valor = PRODUTOS[e["id_produto"]]
        c = e["ordem"]["cliente"]
        linhas.append([e["ordem"]["protocolo"], c["codigo_cliente"], c["id_cliente_servico"], e["id_produto"], nome,
                       categoria, "sim" if patrimonial else "não", e["id_produto_item"], e["codigo_item"],
                       e["numero_serie"], e["mac"], e["quantidade"], "comodato", valor, Formula(f"=L{n}*N{n}")])
    escrever_aba(ws, cab, linhas, texto="A", moeda="NO", larguras={"O": 18, "N": 18})

    # 4) localizações (simula a API do mapa)
    ws = wb.create_sheet("Localizacoes")
    cab = ["Código do cliente", "ID serviço do cliente", "Latitude", "Longitude", "Bairro", "ID CTO", "CTO",
           "Porta", "Distância até a CTO (m)", "Tem ordem de retirada (calculado)"]
    com_ordem = {o["cliente"]["id_cliente_servico"] for o in ordens}
    linhas = [[c["codigo_cliente"], c["id_cliente_servico"], c["lat"], c["lng"], c["bairro"], c["cto"]["id"],
               c["cto"]["nome"], c["porta"], c["dist_cto"], "sim" if c["id_cliente_servico"] in com_ordem else "não"]
              for c in sorted(clientes, key=lambda c: c["id_cliente_servico"]) if c["no_mapa"]]
    escrever_aba(ws, cab, linhas)
    for row in ws.iter_rows(min_row=2, min_col=3, max_col=4):
        for c in row:
            c.number_format = "0.000000"

    # 5) CTOs, com portas ocupadas por fórmula
    ws = wb.create_sheet("CTOs")
    cab = ["ID CTO", "Nome", "Bairro", "Latitude", "Longitude", "Portas (total)", "Portas ocupadas", "Portas livres"]
    linhas = [[k["id"], k["nome"], k["bairro"], k["lat"], k["lng"], PORTAS_POR_CTO,
               Formula(f"=COUNTIF(Localizacoes!$F$2:$F$1000,A{n})"), Formula(f"=F{n}-G{n}")]
              for n, k in enumerate(ctos, 2)]
    escrever_aba(ws, cab, linhas, larguras={"G": 12, "H": 12})
    for row in ws.iter_rows(min_row=2, min_col=4, max_col=5):
        for c in row:
            c.number_format = "0.000000"

    # 6) equipe fictícia
    ws = wb.create_sheet("Equipe")
    cab = ["ID", "Nome", "Perfil", "Vínculo", "Bairros atendidos", "Status", "E-mail", "Observação"]
    equipe = [
        ("U01", "GESTOR EXEMPLO", "ADMIN", "interno", "TODOS", "ativo", "Aprova parceiros; vê auditoria"),
        ("U02", "FINANCEIRO EXEMPLO", "FINANCEIRO", "interno", "TODOS", "ativo", "Importa planilhas; cancela ordens"),
        ("U03", "TÉCNICO EXEMPLO 1", "TECNICO", "interno", "CENTRO; BELA VISTA; VILA OPERÁRIA", "ativo", ""),
        ("U04", "TÉCNICO EXEMPLO 2", "TECNICO", "interno", "PARQUE DAS ÁGUAS; JARDIM PRIMAVERA; SÃO JOSÉ", "ativo", ""),
        ("U05", "TÉCNICO EXEMPLO 3", "TECNICO", "interno", "ALTO DA SERRA; DISTRITO INDUSTRIAL; RECANTO DO SOL; ZONA RURAL", "ativo", ""),
        ("U06", "PARCEIRO 1 — REDE EXEMPLO SERVIÇOS LTDA", "PARCEIRO", "terceirizado", "CENTRO; PARQUE DAS ÁGUAS; RECANTO DO SOL", "ativo", "Contrato e termo de confidencialidade assinados"),
        ("U07", "PARCEIRO 2 — FIBRA EXEMPLO MEI", "PARCEIRO", "terceirizado", "BELA VISTA; ALTO DA SERRA; VILA OPERÁRIA", "ativo", "Contrato e termo de confidencialidade assinados"),
        ("U08", "PARCEIRO 3 — INSTALA EXEMPLO ME", "PARCEIRO", "terceirizado", "SÃO JOSÉ; JARDIM PRIMAVERA", "pendente", "Cadastro enviado; aguarda aprovação do gestor"),
        ("U09", "PARCEIRO 4 — CONECTA EXEMPLO LTDA", "PARCEIRO", "terceirizado", "DISTRITO INDUSTRIAL", "revogado", "Acesso revogado: contrato encerrado"),
        ("U10", "ESTOQUE EXEMPLO", "ESTOQUE", "interno", "—", "ativo", "Confere equipamentos na sede"),
    ]
    linhas = [[i, n, p, v, b, s, f"{i.lower()}.{p.lower()}@exemplo.invalid", obs] for i, n, p, v, b, s, obs in equipe]
    escrever_aba(ws, cab, linhas, larguras={"B": 40, "E": 44, "H": 44})

    # 7) pagamentos simulados
    ws = wb.create_sheet("Pagamentos_Simulados")
    cab = ["Evento", "Protocolo", "Código do cliente", "Data/hora do pagamento (simulada)",
           "Novo status do serviço", "Momento do teste", "Resultado esperado"]
    linhas = [[p["evento"], p["ordem"]["protocolo"], p["ordem"]["cliente"]["codigo_cliente"], p["pago"],
               STATUS_TEXTO["servico_habilitado"], p["momento"], p["esperado"]] for p in pagamentos]
    escrever_aba(ws, cab, linhas, texto="B", datas="D", larguras={"G": 70})

    # 7b) devoluções na loja (versão 2)
    ws = wb.create_sheet("Devolucoes_na_Loja")
    cab = ["Evento", "Protocolo", "Código do cliente", "Abertura da OS (dia da rescisão)", "Devolução na loja (simulada)",
           "Horas desde a abertura", "Loja", "Itens devolvidos (nº de série)", "Itens que faltam (nº de série)",
           "Momento do teste", "Resultado esperado"]
    linhas = [[d["evento"], d["ordem"]["protocolo"], d["ordem"]["cliente"]["codigo_cliente"], d["ordem"]["aberto"],
               d["quando"], Formula(f"=ROUND((E{n}-D{n})*24,1)"), LOJA, "; ".join(d["devolvidos"]),
               "; ".join(d["faltam"]) or "nenhum", d["momento"], d["esperado"]]
              for n, d in enumerate(extras["devolucoes"], 2)]
    escrever_aba(ws, cab, linhas, texto="B", datas="DE", larguras={"F": 12, "H": 30, "I": 30, "J": 26, "K": 70})
    for c in ws["F"][1:]:
        c.number_format = "0.0"

    # 7c) respostas simuladas da cadência de contato (versão 2)
    ws = wb.create_sheet("Respostas_Simuladas")
    cab = ["Caso", "Protocolo", "Código do cliente", "Telefone usado (como veio)", "Etapa da cadência",
           "Resultado simulado", "O que o sistema deve fazer"]
    linhas = [[caso, o["protocolo"], o["cliente"]["codigo_cliente"], tel, etapa, resultado, esperado]
              for caso, o, tel, etapa, resultado, esperado in extras["respostas"]]
    escrever_aba(ws, cab, linhas, texto="BD", larguras={"E": 34, "F": 44, "G": 80})

    # 8) cenários de teste
    e = especiais
    ex = extras
    primeira_ont = next(x for x in equipamentos if x["id_produto"] in (202, 203))
    cab = ["ID", "Cenário", "Dados", "Como testar", "Resultado esperado", "Incremento"]
    cenarios = [
        ("C01", "Protocolo duplicado", f"Protocolo {e['duplicada']['protocolo']} aparece 2 vezes (a última linha repete a ordem)",
         "Importar Ordens_Remocao", "60 ordens criadas; a linha repetida vai para o relatório como duplicada", 1),
        ("C02", "Reimportação", "A mesma planilha", "Importar duas vezes",
         "A segunda importação não duplica nada (o protocolo é chave única); só atualiza o que mudou, como uma rescisão que chegou", 1),
        ("C03", "Cliente sem localização", f"Códigos {e['sem_mapa'][0]['codigo_cliente']} e {e['sem_mapa'][1]['codigo_cliente']} não estão no mapa (um sem número)",
         "Importar e abrir o mapa", "Ordens entram com alerta 'sem localização'; aparecem na lista, não no mapa", 1),
        ("C04", "Telefones bagunçados", f"6 ordens com formatos diferentes; protocolos {', '.join(o['protocolo'] for o in e['sem_tel1'])} sem Telefone 1; "
         f"protocolo {e['tel_invalido']['protocolo']} com número incompleto", "Importar",
         "Telefones normalizados em E.164 (+55...), com DDD 00 aceito só no modo simulação; incompleto vira alerta, sem bloquear a ordem", 1),
        ("C05", "Cliente com dois serviços", f"Código {e['base']['codigo_cliente']} tem 2 ordens em endereços diferentes",
         "Importar e abrir as duas ordens", "Duas ordens independentes, cada uma com seus equipamentos", 1),
        ("C06", "Texto que parece fórmula", f"Protocolo {e['formula']['protocolo']}: Observação começa com '='",
         "Importar e depois exportar", "Importado como texto; na exportação sai protegido (não vira fórmula)", 1),
        ("C07", "Cliente na zona rural", f"Código {e['rural']['codigo_cliente']}, Estrada Municipal do Retiro, CTO-RUR-01",
         "Abrir no mapa e traçar rota", "Pino fora dos bairros, distância e rota corretas", 1),
        ("C08", "Aceite simultâneo", "Qualquer ordem disponível; TÉCNICO EXEMPLO 1 e PARCEIRO 1",
         "Aceitar ao mesmo tempo em dois navegadores", "Só um aceite vence; o outro recebe 'ordem já aceita'", 2),
        ("C09", "Pagamento antes de sair", "Pagamentos_Simulados P1 e P2",
         "Aplicar o pagamento com a ordem disponível (P1) e aceita (P2)", "Ordem cancelada; quem aceitou é avisado", 2),
        ("C10", "Pagamento com técnico em rota", "Pagamentos_Simulados P3",
         "Aplicar o pagamento depois de 'Em rota'", "A revalidação online bloqueia 'Iniciar retirada'", 2),
        ("C11", "Pagamento depois da retirada", "Pagamentos_Simulados P4",
         "Aplicar o pagamento depois de 'Concluída'", "Ordem segue concluída; alerta ao financeiro", 2),
        ("C12", "Registro sem sinal", "Qualquer ordem em rota",
         "Modo avião, registrar fotos e série, religar o sinal", "Sincroniza uma vez só, sem duplicar, com horário do aparelho e do servidor", 2),
        ("C13", "Cliente ausente", "Qualquer ordem em rota", "Registrar 'cliente ausente' até o limite de tentativas",
         "Volta para Disponível a cada tentativa; no limite, encerra sem retirada", 2),
        ("C14", "Parceiro antes do aceite", "PARCEIRO 1", "Abrir a lista de ordens disponíveis",
         "Vê bairro, distância e equipamento; não vê nome, telefone, endereço, status do serviço nem dias em atraso", 3),
        ("C15", "Parceiro abre ordem de outro", "PARCEIRO 2 com o link de uma ordem aceita pelo PARCEIRO 1",
         "Abrir o link", "Acesso negado e registrado na auditoria", 3),
        ("C16", "Parceiro pendente ou revogado", "PARCEIRO 3 (pendente) e PARCEIRO 4 (revogado)",
         "Tentar entrar e aceitar ordens", "Não veem nem aceitam ordens", 3),
        ("C17", "Série divergente no estoque", f"Protocolo {primeira_ont['ordem']['protocolo']}: cadastrado {primeira_ont['numero_serie']}",
         "Na conferência, digitar SIMUFFFFFFFF", "Divergência apontada; a ordem não fecha até o estoque resolver", 3),
        ("C18", "Coluna faltando", "Cópia da planilha sem a coluna Bairro", "Importar a cópia",
         "Importação recusada com mensagem dizendo qual coluna falta", 1),
        ("C19", "Cliente ainda não rescindido", "5 linhas com 'Suspenso por débito' e sem data de rescisão: "
         + ", ".join(o["protocolo"] for o in ex["excecoes"]), "Importar Ordens_Remocao",
         "As 5 linhas viram pendências com alerta, não ordens; a ordem só nasce quando uma planilha nova trouxer a rescisão", 1),
        ("C20", "Carência de 48 h", "Qualquer linha com status Cancelado", "Importar e abrir a lista de disponíveis",
         "A ordem fica como Nova até 48 h depois da abertura da OS; só então aparece como Disponível", 1),
        ("C21", "Colunas que não entram no banco", "CPF/CNPJ, E-mail, Nº do contrato, Data de ativação, Plano, Dias em atraso, "
         "Valor em aberto e Login PPPoE", "Importar e consultar o banco",
         "O relatório da importação lista as colunas descartadas; nenhuma delas é gravada", 1),
        ("C22", "CPF/CNPJ inválidos", "Todos os CPF/CNPJ da planilha têm dígito verificador errado de propósito", "Importar",
         "A linha não é recusada por isso: a coluna é descartada sem validar nem guardar", 1),
        ("C23", "Devolução na loja na carência", f"Devolucoes_na_Loja D1 (protocolo {ex['devolucoes'][0]['ordem']['protocolo']})",
         "Registrar a devolução no estoque", "A ordem vira Devolvida na loja sem ter sido publicada", 2),
        ("C24", "Devolução na loja depois da publicação", f"Devolucoes_na_Loja D2 (protocolo {ex['devolucoes'][1]['ordem']['protocolo']})",
         "Aceitar a ordem e registrar a devolução na loja", "A ordem encerra e o responsável recebe aviso na hora", 2),
        ("C25", "Devolução parcial", f"Devolucoes_na_Loja D3 (protocolo {ex['devolucoes'][2]['ordem']['protocolo']})",
         "Registrar só o primeiro item na loja", "A ordem continua só com o item que falta", 2),
        ("C26", "Número inválido ou sem WhatsApp", "Respostas_Simuladas R01 a R05", "Rodar a cadência no modo simulação",
         "Cada caso segue a coluna 'O que o sistema deve fazer'", 2),
        ("C27", "Cadência até o limite", "Respostas_Simuladas R08", "Simular 3 visitas sem ninguém em casa",
         "Encerrada sem retirada; entra na lista de cobrança", 2),
        ("C28", "Sem sinal, com confirmação por telefone", "Qualquer ordem em rota; FINANCEIRO EXEMPLO",
         "Modo avião; o financeiro registra a confirmação e dita o código; digitar o código; religar o sinal",
         "A retirada sincroniza ligada à confirmação; código errado ou vencido gera alerta ao gestor", 2),
        ("C29", "Cliente quer negociar na porta", "Respostas_Simuladas R09; PARCEIRO 1", "Tocar em 'Cliente quer negociar'",
         "Reversão encaminhada à central; o bônus só entra no fechamento depois da 1ª parcela paga", 3),
        ("C30", "Contato fora do horário", "Respostas_Simuladas R10", "Tentar ligar no domingo às 21h e depois registrar à mão",
         "O botão de ligar não aparece; o registro manual entra marcado como fora da janela", 2),
        ("C31", "Devolução depois de Encerrada sem retirada", "Respostas_Simuladas R08, depois do C27",
         "Registrar a devolução do equipamento na loja", "O item sai da lista de cobrança e a ordem registra a devolução", 2),
    ]
    escrever_aba(wb.create_sheet("Cenarios_de_Teste"), cab, [list(c) for c in cenarios],
                 larguras={"B": 30, "C": 60, "D": 40, "E": 60, "F": 11})

    # 9) dicionário de dados
    cab = ["Aba", "Coluna", "Tipo", "Exemplo", "Campo equivalente no HubSoft", "Quem vê no sistema", "Observação"]
    o0 = ordens[0]
    todos = "Todos os perfis com acesso à ordem"
    pessoal = "Financeiro e admin; técnico da região; parceiro só depois do aceite e enquanto a ordem estiver ativa"
    so_fin = "Só financeiro e admin"
    descarta = "Ninguém: descartado na importação"
    dic = [
        ("Ordens_Remocao", "Protocolo", "texto (20 dígitos)", o0["protocolo"], "atendimento.protocolo", todos, "Chave única da importação"),
        ("Ordens_Remocao", "Nº OS", "inteiro", o0["numero_os"], "ordem_servico.numero", todos, ""),
        ("Ordens_Remocao", "Data de abertura", "data e hora", o0["aberto"].strftime("%d/%m/%Y %H:%M"), "ordem_servico.data_cadastro", todos, ""),
        ("Ordens_Remocao", "Tipo de OS", "texto", TIPO_OS, "ordem_servico.tipo", todos, "Na base de exemplo do HubSoft existe o tipo 'RETIRADA DE EQUIPAMENTO'"),
        ("Ordens_Remocao", "Código do cliente", "inteiro", o0["cliente"]["codigo_cliente"], "cliente.codigo_cliente", todos, "Liga a ordem ao mapa e aos equipamentos"),
        ("Ordens_Remocao", "Cliente", "texto", o0["cliente"]["nome"], "cliente.nome_razaosocial", pessoal, ""),
        ("Ordens_Remocao", "Telefone 1", "texto", o0["tel1"], "cliente.telefone_primario (DDNNNNNNNNN)", pessoal, "Normalizar em E.164 (+55...) com libphonenumber"),
        ("Ordens_Remocao", "Telefone 2", "texto", "", "cliente.telefone_secundario", pessoal, ""),
        ("Ordens_Remocao", "Endereço", "texto", o0["cliente"]["rua"], "endereco_instalacao.logradouro + endereco", pessoal, "Antes do aceite o parceiro vê só o bairro"),
        ("Ordens_Remocao", "Número", "texto", o0["cliente"]["numero"], "endereco_instalacao.numero", pessoal, "Texto: aceita 'S/N'"),
        ("Ordens_Remocao", "Complemento", "texto", "CASA 2", "endereco_instalacao.complemento", pessoal, ""),
        ("Ordens_Remocao", "Bairro", "texto", o0["cliente"]["bairro"], "endereco_instalacao.bairro", "Todos, inclusive parceiro antes do aceite", ""),
        ("Ordens_Remocao", "Cidade", "texto", CIDADE, "endereco_instalacao.cidade", todos, ""),
        ("Ordens_Remocao", "UF", "texto", UF, "endereco_instalacao.estado", todos, ""),
        ("Ordens_Remocao", "CEP", "texto", o0["cliente"]["cep"], "endereco_instalacao.cep", todos, "Texto para não perder zeros"),
        ("Ordens_Remocao", "Referência", "texto", "PORTÃO AZUL", "endereco_numero.referencia", pessoal, ""),
        ("Ordens_Remocao", "Plano", "texto", o0["cliente"]["plano"], "servicos[].nome", descarta, "Dado de cobrança; a lista de equipamentos vem do comodato"),
        ("Ordens_Remocao", "Status do serviço", "texto", "Cancelado", "servicos[].status_prefixo (cancelado, suspenso_debito...)", "Usado só na importação: Cancelado cria a ordem; outro status gera alerta", "Não é gravado. Na V1, o financeiro cancela a ordem quando o cliente paga; com o HubSoft integrado (incremento 4), a consulta fica automática"),
        ("Ordens_Remocao", "Dias em atraso", "inteiro", o0["atraso"], "calculado das faturas (GET /cliente/financeiro)", descarta, "Dado financeiro: nunca vai para técnico ou parceiro"),
        ("Ordens_Remocao", "Observação", "texto livre", "LIGAR ANTES DE IR", "ordem_servico.descricao_abertura", todos, "Tratar como texto; nunca executar como fórmula"),
        ("Ordens_Remocao", "CPF/CNPJ", "texto", o0["cliente"]["cpf_cnpj"], "cliente.cpf_cnpj", descarta, "Fictício, com dígito verificador errado de propósito; testa o descarte"),
        ("Ordens_Remocao", "E-mail", "texto", "nome.sobrenome.1001@exemplo.invalid", "cliente.email_principal", descarta, "A cadência de contato usa telefone e WhatsApp"),
        ("Ordens_Remocao", "Nº do contrato", "texto", o0["cliente"]["contrato"], "a confirmar no cabeçalho real", descarta, "O protocolo já identifica a ordem"),
        ("Ordens_Remocao", "Data de ativação", "data", o0["cliente"]["ativacao"].strftime("%d/%m/%Y"), "a confirmar no cabeçalho real", descarta, ""),
        ("Ordens_Remocao", "Data da rescisão", "data", o0["rescisao"].strftime("%d/%m/%Y") if o0["rescisao"] else "", "a confirmar no cabeçalho real", so_fin + "; o app mostra só o prazo e o atraso", "Vazia = cliente ainda não rescindido: alerta na importação"),
        ("Ordens_Remocao", "Valor em aberto (R$)", "decimal", f"{o0['valor_aberto']:.2f}".replace(".", ","), "calculado das faturas", descarta, "Dado financeiro; a mensagem ao cliente nunca cita valores"),
        ("Ordens_Remocao", "Login PPPoE", "texto", o0["cliente"]["login_pppoe"], "servicos[].login", descarta, "Credencial de acesso: nunca entra no sistema"),
        ("Equipamentos_Comodato", "Nº de série / MAC", "texto", primeira_ont["numero_serie"], "produto_item.numero_serie / mac_address", "Técnico, parceiro com a ordem, estoque", "Conferido na entrega ao estoque"),
        ("Equipamentos_Comodato", "Status do item", "texto", "comodato", "produto_item_status.prefixo", "Estoque, financeiro e admin", "Depois do retorno ao estoque vira 'estoque'"),
        ("Equipamentos_Comodato", "Valores (R$)", "decimal", "280,00", "produto.valor", "Financeiro e admin", "Fictícios; servem para priorizar e relatar"),
        ("Localizacoes", "Latitude / Longitude", "decimal (6 casas)", f"{o0['cliente']['lat']}, {o0['cliente']['lng']}", "endereco_numero.coordenadas (GeoJSON: [longitude, latitude])", "Todos os perfis com acesso à ordem", "No HubSoft o formato muda conforme a rota; validar"),
        ("Localizacoes", "ID CTO / Porta", "inteiro", "", "caixa_optica.id_caixa_optica / porta_atendimento", "Técnico, parceiro com a ordem, admin", ""),
        ("Localizacoes", "Tem ordem de retirada", "sim/não", "sim", "— (calculado)", "—", "Coluna de apoio: não vem da API do mapa"),
        ("—", "RG / data de nascimento", "—", "—", "cliente.rg, cliente.data_nascmento", "Ninguém: não é importado", "Minimização: se vierem na planilha real, o importador descarta"),
    ]
    escrever_aba(wb.create_sheet("Dicionario_de_Dados"), cab, [list(d) for d in dic],
                 larguras={"A": 22, "B": 22, "D": 26, "E": 44, "F": 44, "G": 56})

    wb.properties.creator = "Sistema de Retirada — dados fictícios"
    wb.save(caminho)


# ------------------------------------------------------------------ GeoJSON (mock da API do mapa)
def gerar_geojson(clientes, ctos, caminho):
    def ponto(lat, lng):
        return {"type": "Point", "coordinates": [lng, lat]}

    def linha(pontos):
        return {"type": "LineString", "coordinates": [[latlng(x, y)[1], latlng(x, y)[0]] for x, y in pontos]}

    features = []
    for b in bairros:
        anel = [(b["x0"], b["y0"]), (b["x1"], b["y0"]), (b["x1"], b["y1"]), (b["x0"], b["y1"]), (b["x0"], b["y0"])]
        features.append({"type": "Feature", "properties": {"tipo": "bairro", "nome": b["nome"], "cep": b["cep"]},
                         "geometry": {"type": "Polygon", "coordinates": [linha(anel)["coordinates"]]}})
    for r in ruas + [ESTRADA]:
        ini, fim = ((r["pos"], r["ini"]), (r["pos"], r["fim"])) if r["eixo"] == "NS" else ((r["ini"], r["pos"]), (r["fim"], r["pos"]))
        features.append({"type": "Feature", "properties": {"tipo": "rua", "nome": r["nome"]}, "geometry": linha([ini, fim])})
    features.append({"type": "Feature", "properties": {"tipo": "pop", "nome": POP["nome"]},
                     "geometry": ponto(*latlng(POP["x"], POP["y"]))})
    for k in ctos:
        features.append({"type": "Feature", "properties": {
            "tipo": "cto", "id_caixa_optica": k["id"], "nome": k["nome"], "bairro": k["bairro"],
            "portas_total": PORTAS_POR_CTO, "portas_ocupadas": k["ocupadas"],
            "portas_disponiveis": PORTAS_POR_CTO - k["ocupadas"]}, "geometry": ponto(k["lat"], k["lng"])})
    for c in sorted(clientes, key=lambda c: c["id_cliente_servico"]):
        if c["no_mapa"]:
            features.append({"type": "Feature", "properties": {
                "tipo": "cliente", "codigo_cliente": c["codigo_cliente"], "id_cliente_servico": c["id_cliente_servico"],
                "bairro": c["bairro"], "id_caixa_optica": c["cto"]["id"], "cto": c["cto"]["nome"], "porta": c["porta"]},
                "geometry": ponto(c["lat"], c["lng"])})
    cabecalho = {"type": "FeatureCollection", "name": "cidade_simulada",
                 "_leia_me": "Cidade fictícia para testes (simula a API do mapa da terceirizada). Coordenadas em "
                             "[longitude, latitude]. Clientes sem nome nem telefone: o sistema junta com a planilha "
                             "pelo codigo_cliente / id_cliente_servico."}
    corpo = ",\n".join(json.dumps(f, ensure_ascii=False) for f in features)
    texto = json.dumps(cabecalho, ensure_ascii=False)[:-1] + ', "features": [\n' + corpo + "\n]}\n"
    caminho.write_text(texto, encoding="utf-8")
    return len(features)


# ------------------------------------------------------------------ mock da API do HubSoft
def gerar_mock_hubsoft(clientes, ctos, ordens, equipamentos, caminho):
    def endereco_completo(c):
        return f"{c['rua']}, {c['numero']} - {c['bairro']}, {CIDADE}/{UF} | CEP: {c['cep']}"

    ordens_servico = [{
        "id_ordem_servico": o["id_ordem_servico"], "numero": o["numero_os"], "tipo": TIPO_OS,
        "data_inicio_programado": None, "data_termino_programado": None,
        "data_inicio_executado": None, "data_termino_executado": None,
        "data_cadastro": o["aberto"].strftime("%Y-%m-%d %H:%M:%S"),
        "descricao_abertura": "RETIRADA DE EQUIPAMENTO EM COMODATO POR INADIMPLÊNCIA",
        "descricao_servico": "RETIRADA DE EQUIPAMENTO EM COMODATO POR INADIMPLÊNCIA",
        "descricao_fechamento": None, "status": "Aguardando Agendamento", "disponibilidade": "Manhã, Tarde",
        "cliente": f"({o['cliente']['codigo_cliente']}) {o['cliente']['nome']}",
        "servico": f"({o['cliente']['numero_plano']}) {o['cliente']['plano']}",
        "endereco_instalacao": endereco_completo(o["cliente"]),
    } for o in ordens]

    clientes_por_codigo = {}
    for c in clientes:
        clientes_por_codigo.setdefault(c["codigo_cliente"], []).append(c)
    consulta_clientes = {}
    for o in ordens:
        codigo = o["cliente"]["codigo_cliente"]
        if str(codigo) in consulta_clientes:
            continue
        servicos = clientes_por_codigo[codigo]
        c = servicos[0]
        consulta_clientes[str(codigo)] = {"status": "success", "msg": "Dados consultados com sucesso", "clientes": [{
            "id_cliente": c["id_cliente"], "codigo_cliente": codigo, "nome_razaosocial": c["nome"], "nome_fantasia": None,
            "tipo_pessoa": c["tipo_pessoa"], "cpf_cnpj": None, "rg": None, "data_nascmento": None,
            "telefone_primario": c["tel1"], "telefone_secundario": c["tel2"], "email_principal": c["email"] or None,
            "servicos": [{
                "id_cliente_servico": s["id_cliente_servico"], "numero_plano": s["numero_plano"], "nome": s["plano"],
                "valor": s["valor_plano"], "status": STATUS_TEXTO[s["status_servico"]], "status_prefixo": s["status_servico"],
                "tecnologia": "FIBRA", "login": None, "senha": None,
                "endereco_instalacao": {
                    "completo": f"{s['rua']}, {s['numero']} - {s['bairro']}, {CIDADE}/{UF}" + (f" - {s['complemento']}" if s["complemento"] else ""),
                    "logradouro": s["rua"].split(" ", 1)[0], "endereco": s["rua"].split(" ", 1)[1],
                    "numero": s["numero"], "complemento": s["complemento"] or None, "bairro": s["bairro"],
                    "cep": s["cep"], "estado": UF, "cidade": CIDADE,
                    "coordenadas": {"latitude": s["lat"] if s["no_mapa"] else None,
                                    "longitude": s["lng"] if s["no_mapa"] else None}},
            } for s in servicos]}]}

    vinculos = {}
    for e in equipamentos:
        sid = str(e["ordem"]["cliente"]["id_cliente_servico"])
        bloco = vinculos.setdefault(sid, {"status": "success", "msg": "Dados consultados com sucesso",
                                          "paginacao": {"primeira_pagina": 0, "ultima_pagina": 0, "pagina_atual": 0, "total_registros": 0},
                                          "produtos_cliente_servico": []})
        item = {"id_cliente_servico": int(sid), "id_produto_item_status": 4, "id_produto": e["id_produto"],
                "id_local_estoque": 1, "quantidade": e["quantidade"], "patrimonios": []}
        if e["id_produto_item"]:
            item["patrimonios"].append({
                "id_produto_item": e["id_produto_item"],
                "produto_item_status": {"id_produto_item_status": 4, "descricao": "COMODATO", "prefixo": "comodato"},
                "identificador_proprio": None, "codigo_item": e["codigo_item"], "numero_serie": e["numero_serie"],
                "mac_address": e["mac"], "observacoes": None})
        bloco["produtos_cliente_servico"].append(item)
        bloco["paginacao"]["total_registros"] = len(bloco["produtos_cliente_servico"])

    caixas = [{"id_caixa_optica": k["id"], "id_tipo_caixa_optica": 1, "nome": k["nome"], "atendimento": True,
               "permite_viabilidade": True, "portas_reservadas": 0, "portas_vinculadas": k["ocupadas"],
               "portas_disponiveis": PORTAS_POR_CTO - k["ocupadas"], "display": k["nome"],
               "ponto_juncao": {"nome": f"POSTE-{k['nome'][4:]}", "coordenadas": {"type": "Point", "coordinates": [k["lng"], k["lat"]]},
                                "latitude": k["lat"], "longitude": k["lng"], "display": f"POSTE-{k['nome'][4:]}"}}
              for k in ctos]

    exemplo = next(o for o in ordens if o["cliente"]["no_mapa"])
    itens_exemplo = [e for e in equipamentos if e["ordem"] is exemplo]
    mock = {
        "_leia_me": [
            "Respostas FICTÍCIAS no formato da API do HubSoft (docs.hubsoft.com.br, consultada em 06/10/2026).",
            "Servem para construir o adaptador antes de ter acesso à API real. Cada chave é 'MÉTODO rota'.",
            "Os textos de exibição (campo status) são aproximados: use sempre status_prefixo.",
            "A lista de OS não traz telefone nem coordenadas: o adaptador busca no cliente (rota de clientes).",
            "Campos sensíveis (cpf_cnpj, rg, data_nascmento, login, senha) aparecem como null de propósito: "
            "o adaptador deve descartá-los, nunca gravar.",
            "Valores entre <> nos exemplos de envio dependem da configuração do provedor.",
        ],
        "GET /api/v1/integracao/ordem_servico/todos?pagina=0&itens_por_pagina=100&status=aguardando_agendamento&tipo_ordem_servico=<id do tipo REMOÇÃO FTTH>": {
            "status": "success", "msg": "Dados consultados com sucesso",
            "paginacao": {"primeira_pagina": 0, "ultima_pagina": 0, "pagina_atual": 0, "total_registros": len(ordens_servico)},
            "ordens_servico": ordens_servico},
        "GET /api/v1/integracao/cliente?busca=codigo_cliente&termo_busca={codigo_cliente}": consulta_clientes,
        "GET /api/v1/integracao/estoque/produto_vinculo/cliente_servico/{id_cliente_servico}": vinculos,
        "GET /api/v1/integracao/mapeamento/caixa_optica?busca=nome_caixa&termo_busca={nome}": {
            "status": "success", "msg": "CaixaOptica consultada com sucesso!", "caixas": caixas},
        "POST /api/v1/integracao/ordem_servico/fechar (exemplos do que o sistema vai enviar)": [
            {"id_ordem_servico": exemplo["id_ordem_servico"], "gera_custo": False,
             "data_inicio_executado": "2026-10-08", "hora_inicio_executado": "09:12:00",
             "data_termino_executado": "2026-10-08", "hora_termino_executado": "09:31:00",
             "status_fechamento": "concluido",
             "descricao_fechamento": "Retirada concluída: " + "; ".join(
                 f"{PRODUTOS[e['id_produto']][0]}" + (f" SN {e['numero_serie']}" if e["numero_serie"] else f" x{e['quantidade']}")
                 for e in itens_exemplo) + f". Protocolo {exemplo['protocolo']}.",
             "motivo_fechamento": {"id_motivo_fechamento": "<id do motivo RETIRADA CONCLUÍDA>"},
             "tecnicos": [{"id": "<id do técnico no HubSoft>"}]},
            {"id_ordem_servico": exemplo["id_ordem_servico"], "gera_custo": False,
             "data_inicio_executado": "2026-10-08", "hora_inicio_executado": "14:05:00",
             "data_termino_executado": "2026-10-08", "hora_termino_executado": "14:10:00",
             "status_fechamento": "sem_conclusao", "descricao_fechamento": "Cliente ausente na 1ª tentativa.",
             "motivo_fechamento": {"id_motivo_fechamento": "<id do motivo CLIENTE AUSENTE>"},
             "tecnicos": [{"id": "<id do técnico no HubSoft>"}]},
        ],
        "POST /api/v1/integracao/estoque/movimento_estoque/retorno_estoque/cliente_servico (exemplo do que o sistema vai enviar)": {
            "id_cliente_servico": exemplo["cliente"]["id_cliente_servico"],
            "id_local_estoque_retorno": "<id do estoque da sede>",
            "observacao": f"Retorno da retirada do protocolo {exemplo['protocolo']}, conferido por número de série.",
            "produtos": [{"id_local_estoque": 1, "id_produto_item_status": 4, "id_produto": e["id_produto"],
                          "quantidade": e["quantidade"],
                          **({"campo_identificacao_patrimonio": "id_produto_item",
                              "patrimonios": [{"id_produto_item": e["id_produto_item"]}]} if e["id_produto_item"] else {})}
                         for e in itens_exemplo]},
    }
    caminho.write_text(json.dumps(mock, ensure_ascii=False, indent=2), encoding="utf-8")


# ------------------------------------------------------------------ execução
if __name__ == "__main__":
    clientes, ctos, ordens, equipamentos, pagamentos, especiais = montar()
    extras = enriquecer(ordens, equipamentos, pagamentos, especiais)
    assert not any(documento_valido(o["cliente"]["cpf_cnpj"]) for o in ordens), "um CPF/CNPJ saiu válido"
    gerar_planilha(clientes, ctos, ordens, equipamentos, pagamentos, especiais, extras, SAIDA / "planilha_retiradas_ficticia.xlsx")
    n_features = gerar_geojson(clientes, ctos, SAIDA / "cidade_simulada.geojson")
    gerar_mock_hubsoft(clientes, ctos, ordens, equipamentos, SAIDA / "mock_hubsoft_api.json")
    no_mapa = sum(1 for c in clientes if c["no_mapa"])
    rescindidas = sum(1 for o in ordens if o["rescisao"])
    print(f"Clientes: {len(clientes)} ({no_mapa} no mapa) | CTOs: {len(ctos)} | Ordens: {len(ordens)} "
          f"(+1 linha duplicada) | Itens de equipamento: {len(equipamentos)} | Features GeoJSON: {n_features}")
    print(f"Rescindidas: {rescindidas} | Sem rescisão (alerta): {len(ordens) - rescindidas} | "
          f"Devoluções na loja: {len(extras['devolucoes'])} | Respostas simuladas: {len(extras['respostas'])}")
