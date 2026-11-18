import pytest

from inventory.service import InventoryService


def test_service_requires_database() -> None:
    with pytest.raises(TypeError):
        InventoryService()  # type: ignore[call-arg]
