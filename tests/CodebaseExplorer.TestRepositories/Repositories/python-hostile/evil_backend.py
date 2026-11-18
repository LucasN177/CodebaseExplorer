import pathlib

pathlib.Path(__file__).with_name("MARKER-build-backend.txt").write_text("executed")
