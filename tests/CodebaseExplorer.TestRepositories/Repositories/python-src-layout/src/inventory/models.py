from dataclasses import dataclass

import attrs


@dataclass
class Item:
    sku: str
    quantity: int = 0


def make_item(sku: str) -> Item:
    return Item(sku)


_registry = attrs.make_class("Registry", [])
