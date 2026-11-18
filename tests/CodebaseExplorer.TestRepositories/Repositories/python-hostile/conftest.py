import pathlib

pathlib.Path(__file__).with_name("MARKER-conftest.txt").write_text("executed")
