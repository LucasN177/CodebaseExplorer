import pathlib

pathlib.Path(__file__).with_name("MARKER-sitecustomize.txt").write_text("executed")
