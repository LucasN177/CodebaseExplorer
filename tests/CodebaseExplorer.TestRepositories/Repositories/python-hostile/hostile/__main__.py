import pathlib

pathlib.Path(__file__).with_name("MARKER-main.txt").write_text("executed")
