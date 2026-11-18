import json
import requests
import yaml
from bs4 import BeautifulSoup

from . import storage
from .models import Item, make_item
from inventory.storage.db import Database


class InventoryService:
    def __init__(self, database: Database) -> None:
        self._database = database

    def load(self, url: str) -> list[Item]:
        # import os  (a comment, not an import)
        text = requests.get(url, timeout=5).text
        data = yaml.safe_load(text) or json.loads("[]")
        BeautifulSoup(text, "html.parser")
        return [make_item(sku) for sku in data] + [storage]


def _helper() -> None:
    pass
