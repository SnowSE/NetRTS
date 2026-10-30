"""Builds the Google Colab notebook from its source.

The source, src/NetRts.Server/wwwroot/colab/badger_brawl.py, is plain Python with '# %%' cell
markers (the "percent" format VS Code and Jupytext read). The site's #/colab page shows its cells as
copy-and-paste blocks; this script turns the same cells into badger_brawl.ipynb beside it, which
"Open in Colab" loads. Run it after every change to the source:

    python tools/make_colab_notebook.py
"""
import json
import re
from pathlib import Path

source = Path(__file__).resolve().parent.parent / "src/NetRts.Server/wwwroot/colab/badger_brawl.py"
cells = []
for chunk in re.split(r"^# %%", source.read_text(encoding="utf-8"), flags=re.M)[1:]:
    header, _, body = chunk.partition("\n")
    body = body.strip("\n")
    if header.strip() == "[markdown]":
        text = "\n".join(line[2:] if line.startswith("# ") else line.lstrip("#") for line in body.splitlines())
        cells.append({"cell_type": "markdown", "metadata": {}, "source": text.splitlines(keepends=True)})
    else:
        cells.append({"cell_type": "code", "metadata": {}, "execution_count": None, "outputs": [],
                      "source": body.splitlines(keepends=True)})

notebook = {
    "nbformat": 4, "nbformat_minor": 0,
    "metadata": {"colab": {"provenance": []}, "kernelspec": {"name": "python3", "display_name": "Python 3"},
                 "language_info": {"name": "python"}},
    "cells": cells,
}
target = source.with_suffix(".ipynb")
target.write_text(json.dumps(notebook, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"Wrote {target} ({len(cells)} cells)")
