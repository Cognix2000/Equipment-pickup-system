"""Cria ou atualiza no GitHub os rótulos de .github/rotulos.yml (precisa do gh logado).

    python3 .github/criar-rotulos.py
"""
import re
import subprocess
from pathlib import Path

linhas = (Path(__file__).parent / "rotulos.yml").read_text(encoding="utf-8").splitlines()
padrao = re.compile(r'nome: "([^"]+)", cor: "([0-9A-Fa-f]{6})", descricao: "([^"]*)"')

for linha in linhas:
    achado = padrao.search(linha)
    if not achado:
        continue
    nome, cor, descricao = achado.groups()
    subprocess.run(["gh", "label", "create", nome, "--color", cor, "--description", descricao, "--force"], check=True)
    print(f"ok  {nome}")
