namespace CodebaseExplorer.Analysis.Languages.Python;

/// <summary>Static name tables for Python import resolution (ANALYSIS_ENGINE.md Stage 3, Python).</summary>
internal static class PythonNames
{
    /// <summary>Top-level modules of the CPython standard library (3.8 – 3.13, incl. removed ones); imports of them produce nothing.</summary>
    public static readonly IReadOnlySet<string> StandardLibrary = new HashSet<string>(StringComparer.Ordinal)
    {
        "__future__", "_thread", "abc", "aifc", "argparse", "array", "ast", "asynchat", "asyncio", "asyncore", "atexit",
        "audioop", "base64", "bdb", "binascii", "bisect", "builtins", "bz2", "cProfile", "calendar", "cgi", "cgitb", "chunk",
        "cmath", "cmd", "code", "codecs", "codeop", "collections", "colorsys", "compileall", "concurrent", "configparser",
        "contextlib", "contextvars", "copy", "copyreg", "crypt", "csv", "ctypes", "curses", "dataclasses", "datetime", "dbm",
        "decimal", "difflib", "dis", "distutils", "doctest", "email", "encodings", "ensurepip", "enum", "errno",
        "faulthandler", "fcntl", "filecmp", "fileinput", "fnmatch", "fractions", "ftplib", "functools", "gc", "getopt",
        "getpass", "gettext", "glob", "graphlib", "grp", "gzip", "hashlib", "heapq", "hmac", "html", "http", "idlelib",
        "imaplib", "imghdr", "imp", "importlib", "inspect", "io", "ipaddress", "itertools", "json", "keyword", "lib2to3",
        "linecache", "locale", "logging", "lzma", "mailbox", "mailcap", "marshal", "math", "mimetypes", "mmap",
        "modulefinder", "msilib", "msvcrt", "multiprocessing", "netrc", "nis", "nntplib", "ntpath", "numbers", "opcode",
        "operator", "optparse", "os", "ossaudiodev", "pathlib", "pdb", "pickle", "pickletools", "pipes", "pkgutil",
        "platform", "plistlib", "poplib", "posix", "posixpath", "pprint", "profile", "pstats", "pty", "pwd", "py_compile",
        "pyclbr", "pydoc", "queue", "quopri", "random", "re", "readline", "reprlib", "resource", "rlcompleter", "runpy",
        "sched", "secrets", "select", "selectors", "shelve", "shlex", "shutil", "signal", "site", "smtpd", "smtplib",
        "sndhdr", "socket", "socketserver", "spwd", "sqlite3", "sre_compile", "sre_constants", "sre_parse", "ssl", "stat",
        "statistics", "string", "stringprep", "struct", "subprocess", "sunau", "symtable", "sys", "sysconfig", "syslog",
        "tabnanny", "tarfile", "telnetlib", "tempfile", "termios", "textwrap", "threading", "time", "timeit", "tkinter",
        "token", "tokenize", "tomllib", "trace", "traceback", "tracemalloc", "tty", "turtle", "turtledemo", "types",
        "typing", "unicodedata", "unittest", "urllib", "uu", "uuid", "venv", "warnings", "wave", "weakref", "webbrowser",
        "winreg", "winsound", "wsgiref", "xdrlib", "xml", "xmlrpc", "zipapp", "zipfile", "zipimport", "zlib", "zoneinfo",
    };

    /// <summary>
    /// Import names whose distribution on PyPI has a different name (heuristic, ANALYSIS-012): matches through this
    /// table are <c>Heuristic</c> because several distributions can provide the same import name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ImportToDistribution = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["yaml"] = "PyYAML",
        ["bs4"] = "beautifulsoup4",
        ["PIL"] = "Pillow",
        ["cv2"] = "opencv-python",
        ["sklearn"] = "scikit-learn",
        ["skimage"] = "scikit-image",
        ["dateutil"] = "python-dateutil",
        ["dotenv"] = "python-dotenv",
        ["jwt"] = "PyJWT",
        ["Crypto"] = "pycryptodome",
        ["attr"] = "attrs",
        ["OpenSSL"] = "pyOpenSSL",
        ["magic"] = "python-magic",
        ["serial"] = "pyserial",
        ["usb"] = "pyusb",
        ["docx"] = "python-docx",
        ["pptx"] = "python-pptx",
        ["git"] = "GitPython",
        ["MySQLdb"] = "mysqlclient",
        ["slugify"] = "python-slugify",
        ["multipart"] = "python-multipart",
        ["jose"] = "python-jose",
        ["ldap"] = "python-ldap",
        ["zmq"] = "pyzmq",
        ["win32api"] = "pywin32",
        ["win32con"] = "pywin32",
        ["pythoncom"] = "pywin32",
        ["fitz"] = "PyMuPDF",
        ["telegram"] = "python-telegram-bot",
        ["discord"] = "discord.py",
        ["kafka"] = "kafka-python",
        ["websocket"] = "websocket-client",
        ["socketio"] = "python-socketio",
        ["engineio"] = "python-engineio",
        ["nacl"] = "PyNaCl",
        ["gi"] = "PyGObject",
        ["wx"] = "wxPython",
        ["google.protobuf"] = "protobuf",
    };

    /// <summary>PEP 503 normalization (runs of <c>-</c>, <c>_</c>, <c>.</c> → <c>-</c>, lower case).</summary>
    public static string Normalize(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.Trim(), "[-_.]+", "-").ToLowerInvariant();
}
